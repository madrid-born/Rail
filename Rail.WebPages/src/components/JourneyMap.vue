<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import type { GeoJSONSource } from 'maplibre-gl';
import type { Map as NeshanMap } from '@neshan-maps-platform/maplibre-sdk';
import type { FeatureCollection, LineString, Point } from 'geojson';
import type { MapPoint } from '../services/journey';
import '@neshan-maps-platform/maplibre-sdk/style.css';

const props = defineProps<{ graph: FeatureCollection<Point | LineString>; from: MapPoint; to: MapPoint }>();
const container = ref<HTMLDivElement>();
const loading = ref(true);
const error = ref('');
let map: NeshanMap | undefined;
let disposed = false;
let timeout: ReturnType<typeof setTimeout> | undefined;
async function initialize() {
  map?.remove();
  clearTimeout(timeout);
  loading.value = true;
  error.value = '';
  const apiKey = import.meta.env.VITE_NESHAN_MAP_KEY?.trim();
  if (!apiKey) { loading.value = false; error.value = 'کلید نقشه تنظیم نشده است.'; return; }
  try {
    const { default: sdk } = await import('@neshan-maps-platform/maplibre-sdk');
    if (disposed || !container.value) return;
    map = new sdk.Map({ container: container.value, apiKey,
      style: 'https://static.neshan.org/sdk/maplibre/styles/light.json', center: props.from, zoom: 11, trackResize: true });
    map.addControl(new sdk.NavigationControl({ showCompass: false }), 'top-left');
    new sdk.Marker({ color: '#00636a' }).setLngLat(props.from).addTo(map);
    new sdk.Marker({ color: '#d15b43' }).setLngLat(props.to).addTo(map);
    map.on('load', () => {
      if (!map) return;
      clearTimeout(timeout);
      loading.value = false;
      map.addSource('journey', { type: 'geojson', data: props.graph });
      map.addLayer({ id: 'journey-lines', type: 'line', source: 'journey', filter: ['all', ['==', ['geometry-type'], 'LineString'], ['!=', ['get', 'walking'], true]],
        paint: { 'line-color': ['get', 'color'], 'line-width': 6 } });
      map.addLayer({ id: 'journey-walking', type: 'line', source: 'journey', filter: ['==', ['get', 'walking'], true],
        paint: { 'line-color': '#58716f', 'line-width': 3, 'line-dasharray': [2, 2] } });
      map.addLayer({ id: 'journey-stations', type: 'circle', source: 'journey', filter: ['==', ['geometry-type'], 'Point'],
        paint: { 'circle-color': '#fff', 'circle-radius': 5, 'circle-stroke-color': '#17383e', 'circle-stroke-width': 2 } });
      map.addLayer({ id: 'journey-labels', type: 'symbol', source: 'journey',
        filter: ['all', ['==', ['geometry-type'], 'Point'], ['==', ['get', 'named'], true]],
        layout: { 'text-field': ['get', 'name'], 'text-font': ['Vazir FD Regular'], 'text-size': 14,
          'text-variable-anchor': ['right', 'left', 'top', 'bottom'], 'text-radial-offset': 1, 'text-allow-overlap': true },
        paint: { 'text-color': '#17383e', 'text-halo-color': '#fff', 'text-halo-width': 2 } });
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
watch(() => props.graph, data => { (map?.getSource('journey') as GeoJSONSource | undefined)?.setData(data); });
onBeforeUnmount(() => { disposed = true; clearTimeout(timeout); map?.remove(); });
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
