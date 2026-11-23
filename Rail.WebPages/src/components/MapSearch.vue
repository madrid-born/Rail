<script setup lang="ts">
import { computed, nextTick, ref, shallowRef, watch } from 'vue';
import type { MapPoint } from '../services/journey';
import { loadMapSearch, searchMap } from '../services/mapSearch';
import type { MapSearchKind, MapSearchResult, MapStationResult } from '../services/mapSearch';
import type { NetworkCacheData } from '../services/network';
import { hasStationCoordinates, metroLineColors, stationLabelStyle } from '../services/metroGraph';
import { normalizeStationSearch, searchMapStations } from '../services/stationSearch';
import { stationLabelUrl } from '../services/stationLabelImage';
import { findLocalPlace } from '../services/localPlace';
import AppIcon from './AppIcon.vue';

const props = defineProps<{ getCenter: () => MapPoint | undefined; network?: NetworkCacheData }>();
const emit = defineEmits<{ select: [result: MapSearchResult] }>();

const kinds: Record<MapSearchKind, string> = {
  locality: 'شهر یا آبادی', neighborhood: 'محله یا محدوده', station: 'ایستگاه مترو', bus: 'ایستگاه اتوبوس', street: 'معبر', landmark: 'مکان',
};
const iconPaths = {
  locality: 'M3 21h18M5 21V9h6v12m0 0V3h8v18M8 12v1m0 3v1m7-11v1m0 3v1m0 3v1m0 3v1',
  neighborhood: 'm2 11 6-5 6 5m-11-1v9h10v-9m0-3 3-2 6 5m-1-1v10h-5M7 19v-4h2v4',
  bus: 'M5 17V6a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v11a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1Zm0-5h14M8 18v2m8-2v2M8 15h.01M16 15h.01',
  street: 'M7 3 3 21M17 3l4 18M12 4v2m0 5v2m0 5v2',
  landmark: 'M19 10c0 5-7 10-7 10S5 15 5 10a7 7 0 1 1 14 0ZM12 8a2 2 0 1 0 0 4 2 2 0 0 0 0-4Z',
};
const input = ref<HTMLInputElement>();
const query = ref('');
const open = ref(false);
const active = ref(-1);
const loading = ref(false);
const failed = ref(false);
const index = shallowRef<Awaited<ReturnType<typeof loadMapSearch>>>();
const streetLabels = shallowRef(new Map<string, string | undefined>());
const stationBorders = new Map<string, string>();
const hasQuery = computed(() => normalizeStationSearch(query.value).replaceAll(' ', '').length >= 2);
const stationCandidates = computed(() => props.network?.stations.filter(hasStationCoordinates) ?? []);
const lineColors = computed(() => metroLineColors(props.network?.lines ?? []));
const stationResults = computed<MapStationResult[]>(() => open.value && hasQuery.value
  ? searchMapStations(stationCandidates.value, query.value, props.getCenter()).map(station => ({
    ...stationLabelStyle(station, lineColors.value), name: station.nameFa || station.nameEn, kind: 'station',
    stationId: station.id, point: [station.longitude, station.latitude],
  })) : []);
const results = computed<MapSearchResult[]>(() => open.value && hasQuery.value ? [
  ...stationResults.value,
  ...(index.value ? searchMap(index.value, query.value, props.getCenter(), stationResults.value) : []),
] : []);
const resultKey = (result: MapSearchResult) => result.kind === 'station' ? `station/${result.stationId}`
  : `${result.kind}/${result.point.join(',')}/${result.name}`;
const streetName = (name: string) => normalizeStationSearch(name.split(/\s+[-–—]\s*/u)[0]!)
  .replace(/^(خیابان|بلوار|بزرگراه|کوچه|میدان) /, '').replaceAll(' ', '');

function resultSubtitle(result: MapSearchResult) {
  return streetLabels.value.get(resultKey(result)) || (result.kind !== 'station' && result.area ? `نزدیک ${result.area}` : '');
}

function stationBorder(result: MapStationResult) {
  const key = `${result.labelDashed}/${result.labelColors.join(',')}`;
  let border = stationBorders.get(key);
  if (!border) {
    border = `url("${stationLabelUrl(result.labelColors, result.labelDashed)}")`;
    stationBorders.set(key, border);
  }
  return { '--station-border': border };
}
const status = computed(() => {
  if (!open.value || !hasQuery.value || results.value.length) return '';
  if (loading.value) return 'در حال آماده‌سازی جست‌وجو…';
  if (failed.value) return 'جست‌وجوی نقشه در دسترس نیست.';
  return 'مکانی پیدا نشد.';
});

async function ensureIndex() {
  if (!hasQuery.value || index.value || loading.value) return;
  loading.value = true;
  failed.value = false;
  try { index.value = await loadMapSearch(); }
  catch { failed.value = true; }
  finally { loading.value = false; }
}

function showResults() {
  open.value = true;
  void ensureIndex();
}

function closeResults() {
  open.value = false;
  active.value = -1;
}

function select(result?: MapSearchResult) {
  if (!result) return;
  closeResults();
  query.value = result.name;
  input.value?.blur();
  emit('select', result);
}

function onKeydown(event: KeyboardEvent) {
  if (event.isComposing) return;
  if (event.key === 'Escape' && open.value) {
    event.preventDefault();
    event.stopPropagation();
    closeResults();
    return;
  }
  if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
  showResults();
  const count = results.value.length;
  if (!count) return;
  event.preventDefault();
  const direction = event.key === 'ArrowDown' ? 1 : -1;
  active.value = active.value < 0 ? (direction === 1 ? 0 : count - 1) : (active.value + direction + count) % count;
  void nextTick(() => document.getElementById(`map-search-result-${active.value}`)?.scrollIntoView({ block: 'nearest' }));
}

watch(query, () => {
  active.value = -1;
  if (open.value) void ensureIndex();
});
watch(stationResults, () => { active.value = -1; });

watch(results, (matches, _, cleanup) => {
  if (!matches.length) return;
  const request = new AbortController();
  async function addStreetLabels() {
    for (const result of matches) {
      if (request.signal.aborted) break;
      const key = resultKey(result);
      if (streetLabels.value.has(key)) continue;
      try {
        const name = streetName(result.name);
        const street = await findLocalPlace(result.point, request.signal, {
          exclude: nearby => result.kind === 'street' && streetName(nearby) === name,
        });
        if (request.signal.aborted) break;
        const labels = new Map(streetLabels.value);
        if (labels.size >= 128) labels.delete(labels.keys().next().value!);
        labels.set(key, street);
        streetLabels.value = labels;
      } catch {
        if (request.signal.aborted) break;
      }
    }
  }
  const timer = setTimeout(() => void addStreetLabels(), 160);
  cleanup(() => { clearTimeout(timer); request.abort(); });
});
</script>

<template>
  <form class="map-search" role="search" @submit.prevent="select(results[Math.max(active, 0)])">
    <label class="map-search-field">
      <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 4 4" /></svg>
      <input ref="input" v-model="query" type="search" role="combobox" aria-autocomplete="list" translate="no"
        :aria-expanded="results.length > 0" aria-controls="map-search-results"
        :aria-activedescendant="active >= 0 ? `map-search-result-${active}` : undefined"
        aria-label="جست‌وجو روی نقشه" placeholder="جست‌وجو روی نقشه" autocomplete="off" spellcheck="false"
        @focus="showResults" @input="showResults" @blur="closeResults" @keydown="onKeydown" />
    </label>
    <ul v-show="results.length" id="map-search-results" class="map-search-panel" role="listbox" aria-label="نتایج جست‌وجوی نقشه">
      <li v-for="(result, i) in results" :id="`map-search-result-${i}`" :key="resultKey(result)" role="option"
        :aria-selected="i === active" :aria-label="[result.name, kinds[result.kind], resultSubtitle(result)].filter(Boolean).join('، ')"
        @pointerdown.prevent @click="select(result)">
        <span class="map-search-icon" :data-kind="result.kind" aria-hidden="true">
          <AppIcon v-if="result.kind === 'station'" name="stations" />
          <svg v-else viewBox="0 0 24 24" focusable="false"><path :d="iconPaths[result.kind]" /></svg>
        </span>
        <span class="map-search-copy" translate="no">
          <bdi v-if="result.kind === 'station'" class="map-search-station" :style="stationBorder(result)">{{ result.name }}</bdi>
          <bdi v-else>{{ result.name }}</bdi>
          <small v-if="resultSubtitle(result)"><bdi>{{ resultSubtitle(result) }}</bdi></small>
        </span>
      </li>
    </ul>
    <p v-show="status" class="map-search-panel map-search-status" role="status">{{ status }}</p>
  </form>
</template>

<style scoped>
.map-search { position: relative; flex: 1; min-width: 0; }
.map-search-field {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 46px;
  padding-inline: 14px;
  border: 1px solid var(--border);
  border-radius: 12px;
  background: var(--glass);
  color: var(--muted);
}
.map-search-field svg { flex: none; width: 20px; height: 20px; }
.map-search-field:focus-within { outline: 2px solid var(--teal); outline-offset: -2px; }
.map-search-field input {
  flex: 1;
  align-self: stretch;
  min-width: 0;
  padding: 0;
  border: 0;
  outline: none;
  background: transparent;
  color: var(--ink);
}
.map-search-field input::placeholder { color: var(--muted); }
.map-search-panel {
  position: absolute;
  inset: calc(100% + 6px) 0 auto;
  max-height: min(340px, 40dvh);
  overflow: auto;
  overscroll-behavior: contain;
  list-style: none;
  margin: 0;
  padding: 4px;
  border: 1px solid var(--border);
  border-radius: 12px;
  background: var(--canvas);
  box-shadow: 0 8px 24px #102c3420;
}
.map-search-panel li {
  display: grid;
  grid-template-columns: 34px minmax(0, 1fr);
  align-items: center;
  gap: 10px;
  min-height: 56px;
  padding: 7px 9px;
  border-radius: 8px;
  cursor: pointer;
}
.map-search-panel li + li { border-top: 1px solid var(--border); }
.map-search-panel li:hover, .map-search-panel li[aria-selected='true'] { background: var(--active); }
.map-search-icon {
  display: grid;
  place-items: center;
  width: 34px;
  height: 34px;
  border: 1px solid var(--border);
  border-radius: 10px;
  color: var(--teal);
}
.map-search-icon[data-kind='station'], .map-search-icon[data-kind='bus'] { background: var(--active); }
.map-search-icon svg { width: 19px; height: 19px; }
.map-search-copy { display: grid; line-height: 1.35; text-align: start; }
.map-search-copy > bdi { font-weight: 700; overflow-wrap: anywhere; }
.map-search-station {
  justify-self: start;
  min-height: 28px;
  max-width: 100%;
  padding: 4px 10px;
  border: 1px solid transparent;
  border-image: var(--station-border) 24 32 / 12px 16px stretch;
}
.map-search-copy small { font-size: 12px; margin-top: 3px; overflow-wrap: anywhere; }
.map-search-panel small, .map-search-status { color: var(--muted); }
.map-search-status { padding: 10px 14px; }
</style>
