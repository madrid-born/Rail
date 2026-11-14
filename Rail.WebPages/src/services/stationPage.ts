import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue';
import type { Ref } from 'vue';
import { apiBase } from './api';
import type { MetroLine, MetroRoute, Station } from './network';
import type { StationBoard } from './stationBoard';
import { rememberedStationLine, saveStationLine } from './stationDetails';
import type { StationDetails } from './stationDetails';

export interface StationPageData {
  station: StationDetails;
  selectedLineId: number | null;
  lines: MetroLine[];
  routes: MetroRoute[];
  stations: Station[];
  board: StationBoard | null;
  boardUnavailable: boolean;
}

export function useStationPage(stationId: Ref<string>) {
  const page = shallowRef<StationPageData>();
  const board = shallowRef<StationBoard>();
  const selectedLineId = ref<number>();
  const loading = ref(false);
  const failed = ref(false);
  const boardLoading = ref(false);
  const boardFailed = ref(false);
  const retry = ref(0);
  let boardRequest: AbortController | undefined;
  let timer: ReturnType<typeof setTimeout> | undefined;

  function scheduleRefresh() {
    clearTimeout(timer);
    if (page.value && selectedLineId.value !== undefined && document.visibilityState === 'visible') {
      timer = setTimeout(() => { void refreshBoard(); }, 30_000);
    }
  }

  watch([stationId, retry], async ([id], _, onCleanup) => {
    clearTimeout(timer);
    boardRequest?.abort();
    boardRequest = undefined;
    const request = new AbortController();
    const timeout = setTimeout(() => request.abort(), 20_000);
    let active = true;
    onCleanup(() => { active = false; request.abort(); clearTimeout(timeout); });
    page.value = undefined;
    board.value = undefined;
    selectedLineId.value = undefined;
    loading.value = true;
    failed.value = false;
    boardFailed.value = false;
    boardLoading.value = false;
    try {
      const saved = rememberedStationLine(id);
      const query = saved === undefined ? '' : `?lineId=${saved}`;
      const response = await fetch(`${apiBase}/api/stations/${encodeURIComponent(id)}/page${query}`, {
        signal: request.signal, cache: 'no-store', headers: { Accept: 'application/json' },
      });
      if (response.status === 404) return;
      if (!response.ok) throw new Error(`Station page HTTP ${response.status}`);
      const result: StationPageData = await response.json();
      if (!active) return;
      page.value = result;
      selectedLineId.value = result.selectedLineId ?? undefined;
      board.value = result.board ?? undefined;
      boardFailed.value = result.boardUnavailable;
      // Seeding the selected line never launches a second board request.
    } catch { if (active) failed.value = true; }
    finally {
      clearTimeout(timeout);
      if (active) { loading.value = false; scheduleRefresh(); }
    }
  }, { immediate: true });

  async function refreshBoard() {
    if (!page.value || selectedLineId.value === undefined || document.visibilityState !== 'visible') return;
    clearTimeout(timer);
    boardRequest?.abort();
    const request = new AbortController();
    boardRequest = request;
    const timeout = setTimeout(() => request.abort(), 20_000);
    boardLoading.value = true;
    try {
      const response = await fetch(`${apiBase}/api/timetable/stations/${encodeURIComponent(stationId.value)}/board?lineId=${selectedLineId.value}`, {
        signal: request.signal, cache: 'no-store', headers: { Accept: 'application/json' },
      });
      if (!response.ok) throw new Error(`Station board HTTP ${response.status}`);
      const result: StationBoard = await response.json();
      if (boardRequest !== request) return;
      board.value = result;
      boardFailed.value = false;
    } catch { if (boardRequest === request) boardFailed.value = true; }
    finally {
      clearTimeout(timeout);
      if (boardRequest === request) { boardLoading.value = false; boardRequest = undefined; scheduleRefresh(); }
    }
  }

  function selectLine(id: number) {
    if (id === selectedLineId.value || !page.value?.lines.some(line => line.id === id)) return;
    selectedLineId.value = id;
    saveStationLine(stationId.value, id);
    board.value = undefined;
    boardFailed.value = false;
    void refreshBoard();
  }
  const onResume = () => {
    clearTimeout(timer);
    if (document.visibilityState === 'visible') void refreshBoard();
  };
  onMounted(() => {
    document.addEventListener('visibilitychange', onResume);
  });
  onBeforeUnmount(() => {
    boardRequest?.abort();
    boardRequest = undefined;
    clearTimeout(timer);
    document.removeEventListener('visibilitychange', onResume);
  });
  return { page, board, selectedLineId, loading, failed, boardLoading, boardFailed,
    selectLine, refreshBoard, reload: () => { retry.value++; } };
}
