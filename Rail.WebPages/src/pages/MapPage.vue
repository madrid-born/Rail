<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import type { GeoJSONSource, Map as MetroMap } from 'maplibre-gl';
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url';
import { checkNetworkCache, useNetworkCache } from '../services/network';
import { buildMetroGraph } from '../services/metroGraph';
import { createStationLabelImage } from '../services/stationLabelImage';
import { lastMapView, rememberMapView } from '../services/pageViewState';
import { initializeMapPreferences, useMapPreferences } from '../services/mapPreferences';
import { basemapStyleUrl, localizeBasemap, stationLabelFont } from '../services/basemap';
import 'maplibre-gl/dist/maplibre-gl.css';

const container = ref<HTMLDivElement>();
const router = useRouter();
const { snapshot, failed, loading } = useNetworkCache();
const { preferences, open: filtersOpen, show: showFilters } = useMapPreferences();
const graph = computed(() => snapshot.value ? buildMetroGraph(snapshot.value.data, preferences.value.hiddenLineIds) : undefined);
const mapLoading = ref(true);
const mapError = ref('');
let map: MetroMap | undefined;
let disposed = false;
let attempt = 0;
let timeout: ReturnType<typeof setTimeout> | undefined;
let styleReady = false;

function drawNetwork() {
  if (!map || !styleReady || !graph.value) return;
  for (const feature of graph.value.features) {
    if (feature.geometry.type !== 'Point') continue;
    const imageId = feature.properties!.labelImage as string;
    if (!map.hasImage(imageId)) {
      map.addImage(imageId, createStationLabelImage(feature.properties!.labelColors as string[]), {
        pixelRatio: 2, stretchX: [[32, 96]], stretchY: [[24, 40]], content: [12, 8, 116, 56],
      });
    }
  }
  const source = map.getSource('metro-network') as GeoJSONSource | undefined;
  if (source) source.setData(graph.value);
  else {
    map.addSource('metro-network', { type: 'geojson', data: graph.value });
    map.addLayer({ id: 'metro-lines', type: 'line', source: 'metro-network',
      filter: ['==', ['geometry-type'], 'LineString'],
      paint: { 'line-color': ['get', 'color'], 'line-width': 4,
        'line-opacity': ['case', ['get', 'operational'], 0.9, 0.35] } });
    map.addLayer({ id: 'metro-stations', type: 'circle', source: 'metro-network',
      filter: ['==', ['geometry-type'], 'Point'],
      paint: { 'circle-radius': ['case', ['get', 'interchange'], 6, 4],
        'circle-color': '#fff', 'circle-stroke-color': '#17383e', 'circle-stroke-width': 2 } });
    map.addLayer({ id: 'metro-station-labels', type: 'symbol', source: 'metro-network', minzoom: 11.5,
      filter: ['==', ['geometry-type'], 'Point'],
      layout: {
        'text-field': ['get', 'name'],
        'icon-image': ['get', 'labelImage'],
        'icon-text-fit': 'both',
        'icon-text-fit-padding': [4, 8, 4, 8],
        'icon-allow-overlap': false,
        'icon-padding': 4,
        'text-font': stationLabelFont,
        'text-size': ['interpolate', ['linear'], ['zoom'], 11.5, 12, 15, 15],
        'text-variable-anchor': ['right', 'left', 'top', 'bottom'],
        'text-radial-offset': 1.3,
        'text-justify': 'auto',
        'text-max-width': 10,
        'text-padding': 8,
        'text-allow-overlap': false,
        'symbol-sort-key': ['case', ['get', 'interchange'], 0, 1],
      },
      paint: { 'text-color': '#17383e', 'text-halo-color': '#ffffff', 'text-halo-width': 2 } });
  }
  map.setLayoutProperty('metro-station-labels', 'visibility', preferences.value.showStationNames ? 'visible' : 'none');
  map.setLayoutProperty('metro-station-labels', 'icon-image', preferences.value.showStationNameBoxes ? ['get', 'labelImage'] : undefined);
}

async function initializeMap() {
  const currentAttempt = ++attempt;
  clearTimeout(timeout);
  map?.remove();
  map = undefined;
  styleReady = false;
  mapLoading.value = true;
  mapError.value = '';
  try {
    await initializeMapPreferences();
    const sdk = await import('maplibre-gl');
    if (disposed || currentAttempt !== attempt || !container.value) return;
    sdk.setWorkerUrl(workerUrl);
    map = new sdk.Map({ container: container.value,
      style: basemapStyleUrl,
      transformStyle: (_previous, next) => localizeBasemap(next),
      // Keep the initial city view and restore the camera after station navigation.
      center: [51.40, 35.69], zoom: 10.5, minZoom: 2, maxZoom: 20,
      ...lastMapView,
      trackResize: true });
    map.addControl(new sdk.NavigationControl({ showCompass: false }), 'top-left');
    map.on('style.load', () => {
      styleReady = true;
      drawNetwork();
    });
    map.on('load', () => {
      clearTimeout(timeout);
      mapLoading.value = false;
      mapError.value = '';
      styleReady = true;
      drawNetwork();
    });
    map.on('error', () => {
      mapError.value = 'بارگذاری نقشه کامل نشد. اتصال اینترنت را بررسی کنید.';
      mapLoading.value = false;
      clearTimeout(timeout);
    });
    const stationLayers = ['metro-stations', 'metro-station-labels'];
    map.on('click', stationLayers, event => {
      const stationId = event.features?.[0]?.properties?.stationId;
      if (typeof stationId !== 'string' || !stationId) return;
      void router.push({ name: 'specific-station', params: { stationId } });
    });
    map.on('mouseenter', stationLayers, () => { if (map) map.getCanvas().style.cursor = 'pointer'; });
    map.on('mouseleave', stationLayers, () => { if (map) map.getCanvas().style.cursor = ''; });
    timeout = setTimeout(() => {
      mapLoading.value = false;
      mapError.value = 'دریافت نقشه بیش از حد طول کشید. دوباره تلاش کنید.';
    }, 25_000);
  } catch {
    mapLoading.value = false;
    mapError.value = 'نمایش نقشه در این دستگاه ممکن نیست. اتصال اینترنت و پشتیبانی WebGL را بررسی کنید.';
  }
}

watch([graph, () => preferences.value.showStationNames, () => preferences.value.showStationNameBoxes], drawNetwork);
onMounted(() => { void initializeMap(); });
onBeforeUnmount(() => {
  disposed = true;
  clearTimeout(timeout);
  if (map) rememberMapView({ center: map.getCenter().toArray(), zoom: map.getZoom(),
    bearing: map.getBearing(), pitch: map.getPitch(), padding: map.getPadding() });
  map?.remove();
});
</script>

<template>
  <section class="metro-map" aria-label="نقشه متروی تهران" aria-describedby="map-help">
    <div ref="container" class="metro-map-canvas" aria-label="نقشه تعاملی مترو" />
    <p v-if="mapLoading" class="map-message" role="status">در حال دریافت نقشه…</p>
    <div v-if="mapError" class="map-message" role="alert">
      <p>{{ mapError }}</p>
      <button class="text-button" @click="initializeMap">تلاش دوباره</button>
    </div>
    <button type="button" class="map-filter" aria-label="فیلتر نقشه" title="فیلتر نقشه"
      aria-haspopup="dialog" :aria-expanded="filtersOpen" @click="showFilters">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 5h16l-6 7v6l-4 2v-8Z" /></svg>
    </button>
  <p v-if="loading && !snapshot" class="map-caption" role="status">در حال دریافت ایستگاه‌ها…</p>
  <p v-else-if="failed" class="map-caption" role="alert">
    دریافت ایستگاه‌ها ممکن نشد.
    <button class="text-button" @click="checkNetworkCache">تلاش دوباره</button>
  </p>
  <p id="map-help" class="map-help">برای دیدن جزئیات، ایستگاه را لمس کنید. اتصال‌ها بین مختصات ایستگاه‌ها رسم شده‌اند و مسیر دقیق ریل نیستند.</p>
  </section>
</template>

<style scoped>
.metro-map { position: absolute; inset: 0; overflow: hidden; background: var(--surface); }
.metro-map-canvas { width: 100%; height: 100%; direction: ltr; }
.map-message { position: absolute; z-index: 1; top: calc(70px + env(safe-area-inset-top, 0px)); right: calc(12px + env(safe-area-inset-right, 0px)); left: calc(12px + env(safe-area-inset-left, 0px)); padding: 12px 16px; border-radius: 14px; background: var(--glass); color: var(--ink); }
.map-filter { position: absolute; top: calc(12px + env(safe-area-inset-top, 0px)); right: calc(12px + env(safe-area-inset-right, 0px)); display: grid; place-items: center; border: 1px solid var(--border); border-radius: 12px; width: 46px; height: 46px; background: var(--glass); color: var(--ink); }
.map-caption { position: absolute; bottom: calc(145px + env(safe-area-inset-bottom, 0px)); inset-inline: 16px; width: fit-content; margin-inline: auto; padding: 8px 14px; border-radius: 12px; background: var(--glass); color: var(--ink); font-size: 13px; }
.map-help { position: absolute; width: 1px; height: 1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
.metro-map :deep(.maplibregl-ctrl-top-left) { top: env(safe-area-inset-top, 0px); left: env(safe-area-inset-left, 0px); }
.metro-map :deep(.maplibregl-ctrl-bottom-left), .metro-map :deep(.maplibregl-ctrl-bottom-right) { bottom: calc(112px + env(safe-area-inset-bottom, 0px)); }
.metro-map :deep(.maplibregl-ctrl button svg) { width: auto; height: auto; }
</style>
