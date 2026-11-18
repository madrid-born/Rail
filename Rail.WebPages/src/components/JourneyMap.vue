<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import type { GeoJSONSource } from 'maplibre-gl';
import type { Map as MapLibreMap } from 'maplibre-gl';
import type { FeatureCollection, LineString, Point } from 'geojson';
import { installMapStationSelection } from '../services/mapStationSelection';
import { createStationLabelImage } from '../services/stationLabelImage';
import type { MapPoint } from '../services/journey';

const props = defineProps<{ graph: FeatureCollection<Point | LineString>; from: MapPoint; to: MapPoint }>();
const container = ref<HTMLDivElement>();
const router = useRouter();
const loading = ref(true);
const error = ref('');
let map: MapLibreMap | undefined;
let stationSelection: ReturnType<typeof installMapStationSelection> | undefined;
let disposed = false;
let timeout: ReturnType<typeof setTimeout> | undefined;
function addLabelImages(graph: FeatureCollection<Point | LineString>) {
  if (!map) return;
  for (const feature of graph.features) {
    if (feature.geometry.type !== 'Point' || !feature.properties?.named) continue;
    const imageId = feature.properties.labelImage as string;
    if (!imageId || map.hasImage(imageId)) continue;
    map.addImage(imageId, createStationLabelImage(feature.properties.labelColors as string[]), {
      pixelRatio: 2, stretchX: [[32, 96]], stretchY: [[24, 40]], content: [12, 8, 116, 56],
    });
  }
}
async function initialize() {
  stationSelection?.dispose();
  map?.remove();
  clearTimeout(timeout);
  loading.value = true;
  error.value = '';
  try {
    const { sdk, createMapStyle, mapBounds } = await import('../services/map');
    const style = await createMapStyle();
    if (disposed || !container.value) return;
    map = new sdk.Map({ container: container.value, maxBounds: mapBounds,
      style, center: props.from, zoom: 11, trackResize: true });
    stationSelection = installMapStationSelection(map, stationId => {
      void router.push({ name: 'specific-station', params: { stationId } });
    });
    map.addControl(new sdk.NavigationControl({ showCompass: false }), 'top-left');
    new sdk.Marker({ color: '#00636a' }).setLngLat(props.from).addTo(map);
    new sdk.Marker({ color: '#d15b43' }).setLngLat(props.to).addTo(map);
    map.on('load', () => {
      if (!map) return;
      clearTimeout(timeout);
      loading.value = false;
      addLabelImages(props.graph);
      map.addSource('journey', { type: 'geojson', data: props.graph });
      map.addLayer({ id: 'journey-lines', type: 'line', source: 'journey', filter: ['all', ['==', ['geometry-type'], 'LineString'], ['!=', ['get', 'walking'], true], ['==', ['get', 'hasGeometry'], true]],
        layout: { 'line-cap': 'round', 'line-join': 'round' },
        paint: { 'line-color': ['get', 'color'], 'line-width': 6 } });
      map.addLayer({ id: 'journey-lines-undefined', type: 'line', source: 'journey',
        filter: ['all', ['==', ['geometry-type'], 'LineString'], ['!=', ['get', 'walking'], true], ['==', ['get', 'hasGeometry'], false]],
        paint: { 'line-color': ['get', 'color'], 'line-width': 6, 'line-dasharray': [2, 2] } });
      map.addLayer({ id: 'journey-walking', type: 'line', source: 'journey', filter: ['==', ['get', 'walking'], true],
        layout: { 'line-cap': 'round', 'line-join': 'round' },
        paint: { 'line-color': '#4285f4', 'line-width': 5, 'line-dasharray': [0, 2] } });
      map.addLayer({ id: 'journey-stations', type: 'circle', source: 'journey', filter: ['==', ['geometry-type'], 'Point'],
        paint: { 'circle-color': '#fff', 'circle-radius': 5, 'circle-stroke-color': '#17383e', 'circle-stroke-width': 2 } });
      map.addLayer({ id: 'journey-labels', type: 'symbol', source: 'journey',
        filter: ['all', ['==', ['geometry-type'], 'Point'], ['==', ['get', 'named'], true]],
        layout: { 'text-field': ['get', 'name'], 'text-font': ['Noto Sans Regular'], 'text-size': 14,
          'icon-image': ['get', 'labelImage'], 'icon-text-fit': 'both', 'icon-text-fit-padding': [4, 8, 4, 8],
          'icon-allow-overlap': true,
          'text-variable-anchor': ['right', 'left', 'top', 'bottom'], 'text-radial-offset': 1.3, 'text-allow-overlap': true },
        paint: { 'text-color': '#17383e', 'text-halo-color': '#fff', 'text-halo-width': 2 } });
      const stationLayers = ['journey-labels', 'journey-stations'];
      map.on('click', stationLayers, event => {
        const stationId = event.features?.[0]?.properties?.stationId;
        if (typeof stationId === 'string' && stationId)
          stationSelection?.select(stationId);
      });
      map.on('mouseenter', stationLayers, () => { if (map) map.getCanvas().style.cursor = 'pointer'; });
      map.on('mouseleave', stationLayers, () => { if (map) map.getCanvas().style.cursor = ''; });
      const bounds = new sdk.LngLatBounds(props.from, props.from);
      bounds.extend(props.to);
      for (const feature of props.graph.features) {
        if (feature.geometry.type === 'Point') bounds.extend(feature.geometry.coordinates as MapPoint);
        else for (const point of feature.geometry.coordinates) bounds.extend(point as MapPoint);
      }
      map.fitBounds(bounds, { padding: 60, maxZoom: 14, duration: 0 });
    });
    map.on('error', () => { error.value = 'بارگذاری نقشه کامل نشد. دوباره تلاش کنید.'; loading.value = false; clearTimeout(timeout); });
    timeout = setTimeout(() => { loading.value = false; error.value = 'دریافت نقشه بیش از حد طول کشید.'; }, 25_000);
  } catch { loading.value = false; error.value = 'نمایش نقشه ممکن نیست.'; }
}
onMounted(initialize);
watch(() => props.graph, data => {
  const source = map?.getSource('journey') as GeoJSONSource | undefined;
  if (!source) return;
  addLabelImages(data);
  source.setData(data);
});
onBeforeUnmount(() => { disposed = true; clearTimeout(timeout); stationSelection?.dispose(); map?.remove(); });
</script>

<template>
  <section class="journey-map" aria-label="نقشه مسیر سفر">
    <div ref="container" class="canvas" />
    <p v-if="loading" class="message" role="status">در حال دریافت نقشه…</p>
    <div v-if="error" class="message" role="alert">{{ error }} <button class="text-button" @click="initialize">تلاش دوباره</button></div>
  </section>
</template>

<style scoped>
.journey-map { position: relative; height: clamp(300px, 45dvh, 500px); overflow: hidden; border-radius: 22px; border: 1px solid var(--border); }
.canvas { height: 100%; width: 100%; direction: ltr; }
.message { position: absolute; top: 12px; inset-inline: 12px; padding: 12px; border-radius: 12px; background: var(--glass); }
.journey-map :deep(.maplibregl-ctrl button svg) { width: auto; height: auto; }
</style>
