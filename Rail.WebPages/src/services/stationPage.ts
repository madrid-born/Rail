import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue';
import type { Ref } from 'vue';
import { apiBase } from './api';
import type { MetroLine, MetroRoute, Station } from './network';
import type { StationBoard } from './stationBoard';
import { preferredStationLine, rememberedStationLine, saveStationLine } from './stationDetails';
import type { StationDetails } from './stationDetails';
import { useNetworkCache } from './network';
import { useTimetableCache } from './timetable';
import type { TimetableTrip } from './timetable';

export interface StationPageData {
  station: StationDetails;
  selectedLineId: number | null;
  lines: MetroLine[];
  routes: MetroRoute[];
  stations: Station[];
  board: StationBoard | null;
  boardUnavailable: boolean;
}

interface PreviewNetwork { station?: Station; stations: Station[]; lines: MetroLine[]; routes: MetroRoute[] }
export interface StationPreview extends PreviewNetwork {
  station: Station;
  selectedLineId: number | undefined;
  board: StationBoard | undefined;
}
const pageCache = new Map<string, StationPageData>();

export function createStationPreview(id: string, network?: PreviewNetwork, previousBoard?: StationBoard,
    timetable?: { requestedAtUtc: string; trips: readonly TimetableTrip[] }): StationPreview | undefined {
  const station = network?.stations.find(item => item.id === id) ?? (network?.station?.id === id ? network.station : undefined);
  if (!station || !network) return;
  const lines = network.lines.filter(line => station.lines.some(item => item.lineId === line.id)).sort((a, b) => a.id - b.id);
  const selectedLineId = preferredStationLine(id, lines.map(line => line.id));
  let board: StationBoard | undefined;
  if (previousBoard && previousBoard.lineId === selectedLineId) {
    board = { ...previousBoard, stationId: id, nextArrivals: [] };
  } else if (timetable && selectedLineId !== undefined) {
    board = { stationId: id, lineId: selectedLineId, requestedAt: timetable.requestedAtUtc, until: timetable.requestedAtUtc,
      nextArrivals: [], trips: timetable.trips.filter(trip => trip.lineId === selectedLineId)
        .map(trip => ({ ...trip, serviceDate: '', stops: [] })) };
  }
  return { ...network, station, lines, selectedLineId, board };
}

export function useStationPage(stationId: Ref<string>) {
  const { snapshot: network } = useNetworkCache();
  const { snapshot: timetable } = useTimetableCache();
  const page = shallowRef<StationPageData>();
  const preview = shallowRef<StationPreview>();
  const entering = ref(false);
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
    const cached = pageCache.get(id);
    preview.value = createStationPreview(id, cached ?? page.value ?? network.value?.data,
      cached?.board ?? board.value, timetable.value);
    entering.value = true;
    const entranceTimer = setTimeout(() => { entering.value = false; }, 2_000);
    let active = true;
    onCleanup(() => { active = false; request.abort(); clearTimeout(timeout); clearTimeout(entranceTimer); });
    page.value = undefined;
    board.value = preview.value?.board;
    selectedLineId.value = preview.value?.selectedLineId;
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
      if (response.status === 404) { preview.value = undefined; board.value = undefined; return; }
      if (!response.ok) throw new Error(`Station page HTTP ${response.status}`);
      const result: StationPageData = await response.json();
      if (!active) return;
      page.value = result;
      pageCache.set(id, result);
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
    if (loading.value || !page.value || selectedLineId.value === undefined || document.visibilityState !== 'visible') return;
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
      pageCache.set(stationId.value, { ...page.value!, selectedLineId: selectedLineId.value ?? null, board: result });
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
  return { page, preview, entering, board, selectedLineId, loading, failed, boardLoading, boardFailed,
    selectLine, refreshBoard, reload: () => { retry.value++; } };
}
