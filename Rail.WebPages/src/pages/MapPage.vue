<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import type { GeoJSONSource, Map as MapLibreMap, Marker } from 'maplibre-gl';
import MapSearch from '../components/MapSearch.vue';
import MapAlert from '../components/MapAlert.vue';
import { checkNetworkCache, useNetworkCache } from '../services/network';
import { buildMetroGraph } from '../services/metroGraph';
import { createStationLabelImage } from '../services/stationLabelImage';
import { lastMapView, rememberMapView, mapTripSelection } from '../services/pageViewState';
import { initializeMapPreferences, useMapPreferences } from '../services/mapPreferences';
import { parseMapPoint, type MapPoint } from '../services/journey';
import { installMapStationSelection } from '../services/mapStationSelection';
import { installMapLongPress } from '../services/mapLongPress';
import type { MapSearchResult } from '../services/mapSearch';

const container = ref<HTMLDivElement>();
const router = useRouter();
const route = useRoute();
const editingPoint = computed(() => route.query.pick === 'from' ? 'from' : route.query.pick === 'to' ? 'to' : undefined);
const editingLabel = computed(() => editingPoint.value === 'from' ? 'مبدأ' : 'مقصد');
const { snapshot, failed, loading } = useNetworkCache();
const { preferences, open: filtersOpen, show: showFilters } = useMapPreferences();
const graph = computed(() => snapshot.value ? buildMetroGraph(snapshot.value.data, preferences.value.hiddenLineIds) : undefined);
const mapLoading = ref(true);
const mapError = ref('');
const locationMessage = ref('');
const locationSettings = ref<HTMLDialogElement>();
const nativeLocationSettings = ref(false);
const openingLocationSettings = ref(false);
const locationSettingsFailed = ref(false);
const locatingOrigin = ref(false);
const selectedPoint = ref<MapPoint | undefined>(editingPoint.value ? parseMapPoint(route.query[editingPoint.value]) : mapTripSelection.value.from);
const selectedDestination = ref<MapPoint | undefined>(editingPoint.value ? undefined : mapTripSelection.value.to);
const choosingDestination = ref(false);
const selectedStationId = ref<string | undefined>(editingPoint.value ? undefined : mapTripSelection.value.stationId);
const destinationStationId = ref<string | undefined>(editingPoint.value ? undefined : mapTripSelection.value.destinationStationId);
let pin: Marker | undefined;
let destinationPin: Marker | undefined;
let pendingSearchView: { center: MapPoint; zoom: number } | undefined;
let map: MapLibreMap | undefined;
let disposed = false;
let attempt = 0;
let timeout: ReturnType<typeof setTimeout> | undefined;
let styleReady = false;
let longPress: ReturnType<typeof installMapLongPress> | undefined;
let stationSelection: ReturnType<typeof installMapStationSelection> | undefined;

function getMapCenter() { return map?.getCenter().toArray(); }

function requestLocationSettings() {
  locationSettingsFailed.value = false;
  nativeLocationSettings.value = !!window.HybridWebView;
  if (locationSettings.value && !locationSettings.value.open) locationSettings.value.showModal();
}

async function openLocationSettings() {
  if (openingLocationSettings.value) return;
  openingLocationSettings.value = true;
  locationSettingsFailed.value = false;
  try {
    if (await window.HybridWebView?.InvokeDotNet('OpenLocationSettings') === true) locationSettings.value?.close();
    else locationSettingsFailed.value = true;
  } catch {
    locationSettingsFailed.value = true;
  } finally {
    openingLocationSettings.value = false;
  }
}

function selectPoint(point: MapPoint, stationId?: string) {
  pendingSearchView = undefined;
  stationSelection?.cancel();
  if (!editingPoint.value) {
    if (selectedPoint.value && (choosingDestination.value || selectedDestination.value)) {
      selectedDestination.value = point;
      destinationStationId.value = stationId;
      choosingDestination.value = false;
      return;
    }
    selectedDestination.value = undefined;
    destinationStationId.value = undefined;
  }
  selectedPoint.value = point;
  selectedStationId.value = stationId;
}

function showSearchResult(result: MapSearchResult) {
  selectPoint(result.point, result.kind === 'station' ? result.stationId : undefined);
  const minimumZoom = result.kind === 'locality' ? 12 : result.kind === 'neighborhood' ? 14 : 16;
  const view = { center: result.point, zoom: Math.max(map?.getZoom() ?? 0, minimumZoom) };
  if (map) map.flyTo(view);
  else pendingSearchView = view;
}

function updateSelectionPins() {
  if (!map || !pin || !destinationPin) return;
  if (selectedPoint.value) pin.setLngLat(selectedPoint.value).addTo(map);
  else pin.remove();
  if (selectedDestination.value) destinationPin.setLngLat(selectedDestination.value).addTo(map);
  else destinationPin.remove();
}

watch([selectedPoint, selectedDestination], updateSelectionPins);
watch([selectedPoint, selectedDestination, selectedStationId, destinationStationId], () => {
  if (!editingPoint.value) mapTripSelection.value = { ...mapTripSelection.value,
    from: selectedPoint.value, to: selectedDestination.value, stationId: selectedStationId.value,
    destinationStationId: destinationStationId.value };
}, { flush: 'sync' });

function swapEndpoints() {
  const origin = selectedPoint.value;
  const destination = selectedDestination.value;
  if (!origin || !destination) return;
  const originStationId = selectedStationId.value;
  selectedPoint.value = destination;
  selectedDestination.value = origin;
  selectedStationId.value = destinationStationId.value;
  destinationStationId.value = originStationId;
}

async function selectDestinationFromMyLocation() {
  const destination = selectedPoint.value;
  if (!destination || locatingOrigin.value || editingPoint.value || choosingDestination.value || selectedDestination.value) return;
  const stillSelecting = () => !disposed && selectedPoint.value === destination
    && !editingPoint.value && !choosingDestination.value && !selectedDestination.value;
  locatingOrigin.value = true;
  locationMessage.value = '';
  try {
    const { readMapLocation } = await import('../services/mapLocation');
    const origin = await readMapLocation();
    if (!stillSelecting()) return;
    if (typeof origin === 'string') { locationMessage.value = origin; return; }
    destinationStationId.value = selectedStationId.value;
    selectedDestination.value = destination;
    selectedPoint.value = origin;
    selectedStationId.value = undefined;
  } catch {
    if (stillSelecting()) locationMessage.value = 'موقعیت فعلی در دسترس نیست.';
  } finally {
    locatingOrigin.value = false;
  }
}

function startTrip() {
  if (!selectedPoint.value || !selectedDestination.value) return;
  const query: import('vue-router').LocationQueryRaw = { ...mapTripSelection.value.query, ...route.query,
    from: selectedPoint.value.join(','), to: selectedDestination.value.join(',') };
  delete query.pick;
  void router.push({ name: 'new-trip', query });
}

function handleBack(event: Event) {
  if (!selectedPoint.value && !editingPoint.value) return;
  event.preventDefault();
  cancelSelection();
}

function finishPointEdit(save: boolean) {
  const point = editingPoint.value;
  if (!point || (save && !selectedPoint.value)) return;
  const query = { ...route.query };
  delete query.pick;
  delete query.transferMinutes;
  if (save) query[point] = selectedPoint.value!.join(',');
  void router.replace({ name: 'new-trip', query });
}
function cancelSelection() {
  pendingSearchView = undefined;
  if (editingPoint.value) { finishPointEdit(false); return; }
  if (selectedDestination.value) {
    selectedDestination.value = undefined;
    destinationStationId.value = undefined;
    return;
  }
  if (choosingDestination.value) { choosingDestination.value = false; return; }
  selectedPoint.value = undefined;
  selectedStationId.value = undefined;
}

function drawNetwork() {
  if (!map || !styleReady || !graph.value) return;
  for (const feature of graph.value.features) {
    if (feature.geometry.type !== 'Point') continue;
    const imageId = feature.properties!.labelImage as string;
    if (!map.hasImage(imageId)) {
      map.addImage(imageId, createStationLabelImage(feature.properties!.labelColors as string[], feature.properties!.labelDashed === true), {
        pixelRatio: 2, stretchX: [[32, 96]], stretchY: [[24, 40]], content: [12, 8, 116, 56],
      });
    }
  }
  const source = map.getSource('metro-network') as GeoJSONSource | undefined;
  if (source) source.setData(graph.value);
  else {
    map.addSource('metro-network', { type: 'geojson', data: graph.value });
    map.addLayer({ id: 'metro-lines', type: 'line', source: 'metro-network',
      filter: ['all', ['==', ['geometry-type'], 'LineString'], ['==', ['get', 'hasGeometry'], true]],
      paint: { 'line-color': ['get', 'color'], 'line-width': 4,
        'line-opacity': 0.9 } });
    map.addLayer({ id: 'metro-lines-undefined', type: 'line', source: 'metro-network',
      filter: ['all', ['==', ['geometry-type'], 'LineString'], ['==', ['get', 'hasGeometry'], false]],
      paint: { 'line-color': ['get', 'color'], 'line-width': 4, 'line-dasharray': [2, 2],
        'line-opacity': 0.9 } });
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
        // Use the locally hosted glyphs shared with the basemap.
        'text-font': ['Noto Sans Regular'],
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
  longPress?.dispose();
  stationSelection?.dispose();
  pin?.remove();
  destinationPin?.remove();
  map?.remove();
  pin = undefined;
  destinationPin = undefined;
  map = undefined;
  styleReady = false;
  mapLoading.value = true;
  mapError.value = '';
  locationMessage.value = '';
  try {
    await initializeMapPreferences();
    const [{ sdk, createMapStyle, mapBounds }, { createLocationControl }] = await Promise.all([
      import('../services/map'), import('../services/mapLocation'),
    ]);
    const style = await createMapStyle();
    if (disposed || currentAttempt !== attempt || !container.value) return;
    map = new sdk.Map({ container: container.value, maxBounds: mapBounds,
      style,
      // Start over Tehran at city scale; fitting the entire network is opt-in.
      center: [51.40, 35.69], zoom: 10.5, minZoom: 2, maxZoom: 20,
      ...lastMapView,
      ...(editingPoint.value && selectedPoint.value ? { center: selectedPoint.value, zoom: 14 } : {}),
      ...pendingSearchView,
      trackResize: true });
    pendingSearchView = undefined;
    pin = new sdk.Marker({ color: editingPoint.value === 'to' ? '#d15b43' : '#00636a' });
    destinationPin = new sdk.Marker({ color: '#d15b43' });
    updateSelectionPins();
    stationSelection = installMapStationSelection(map, stationId => {
      void router.push({ name: 'specific-station', params: { stationId } });
    });
    map.addControl(new sdk.NavigationControl({ showCompass: false }), 'top-left');
    map.addControl(createLocationControl(message => { locationMessage.value = message; }, requestLocationSettings), 'top-left');
    map.on('style.load', () => {
      styleReady = true;
      drawNetwork();
    });
    map.on('load', () => {
      clearTimeout(timeout);
      mapLoading.value = false;
      styleReady = true;
      drawNetwork();
    });
    map.on('error', () => {
      mapError.value = 'بارگذاری نقشه کامل نشد. اتصال به سرور نقشه را بررسی کنید.';
      mapLoading.value = false;
      clearTimeout(timeout);
    });
    const stationLayers = ['metro-stations', 'metro-station-labels'];
    const selectMapPixel = (pixel: [number, number]) => {
      if (!map) return;
      const coordinate = map.unproject(pixel).wrap();
      const stationLayerIds = stationLayers.filter(id => map?.getLayer(id));
      const stationId = stationLayerIds.length
        ? map.queryRenderedFeatures(pixel, { layers: stationLayerIds })[0]?.properties?.stationId
        : undefined;
      selectPoint([coordinate.lng, coordinate.lat], typeof stationId === 'string' ? stationId : undefined);
    };
    longPress = installMapLongPress(map.getCanvas(), selectMapPixel);
    map.on('dragstart', () => longPress?.cancel());
    map.on('zoomstart', () => longPress?.cancel());
    map.on('rotatestart', () => longPress?.cancel());
    map.on('click', event => {
      if (longPress?.suppressClick()) return;
      if (choosingDestination.value || selectedDestination.value || editingPoint.value) {
        selectMapPixel([event.point.x, event.point.y]);
        return;
      }
      const layers = stationLayers.filter(id => map?.getLayer(id));
      const stationId = layers.length ? map?.queryRenderedFeatures(event.point, { layers })[0]?.properties?.stationId : undefined;
      if (typeof stationId === 'string' && stationId) stationSelection?.select(stationId);
    });
    if (editingPoint.value) {
      const other = parseMapPoint(route.query[editingPoint.value === 'from' ? 'to' : 'from']);
      if (other) new sdk.Marker({ color: editingPoint.value === 'from' ? '#d15b43' : '#00636a' }).setLngLat(other).addTo(map);
    }
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
onMounted(() => { window.addEventListener('rail:back', handleBack); void initializeMap(); });
onBeforeUnmount(() => {
  window.removeEventListener('rail:back', handleBack);
  longPress?.dispose();
  stationSelection?.dispose();
  disposed = true;
  clearTimeout(timeout);
  if (map) rememberMapView({ center: map.getCenter().toArray(), zoom: map.getZoom(),
    bearing: map.getBearing(), pitch: map.getPitch(), padding: map.getPadding() });
  pin?.remove();
  destinationPin?.remove();
  map?.remove();
});
</script>

<template>
  <section class="metro-map" aria-label="نقشه متروی تهران" aria-describedby="map-help">
    <div ref="container" class="metro-map-canvas" aria-label="نقشه تعاملی تهران و حومه" />
    <div class="map-notices">
      <MapAlert class="map-message" :message="mapError || (mapLoading ? 'در حال دریافت نقشه…' : '')" :role="mapError ? 'alert' : 'status'">
        <button v-if="mapError" class="text-button" type="button" @click="initializeMap">تلاش دوباره</button>
      </MapAlert>
      <MapAlert class="map-location-message" :message="locationMessage" role="status" />
    </div>
    <div class="map-toolbar">
      <button type="button" class="map-filter" aria-label="فیلتر نقشه" title="فیلتر نقشه"
        aria-haspopup="dialog" :aria-expanded="filtersOpen" @click="showFilters">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 5h16l-6 7v6l-4 2v-8Z" /></svg>
      </button>
      <MapSearch :get-center="getMapCenter" :network="snapshot?.data" @select="showSearchResult" />
    </div>
    <dialog ref="locationSettings" class="location-settings" aria-labelledby="location-settings-title">
      <h2 id="location-settings-title">روشن کردن موقعیت‌یاب</h2>
      <p>موقعیت‌یاب (GPS) را در تنظیمات دستگاه روشن کنید و به نقشه برگردید.</p>
      <p v-if="locationSettingsFailed" role="status">تنظیمات باز نشد. آن را از دستگاه باز کنید.</p>
      <form method="dialog" class="location-settings-actions">
        <button v-if="nativeLocationSettings" class="button" type="button" autofocus :disabled="openingLocationSettings" @click="openLocationSettings">باز کردن تنظیمات</button>
        <button class="text-button" type="submit">{{ nativeLocationSettings ? 'فعلاً نه' : 'متوجه شدم' }}</button>
      </form>
    </dialog>
    <section v-if="selectedPoint || editingPoint" class="point-selection" aria-label="انتخاب مسیر" aria-live="polite">
      <strong>{{ selectedDestination ? 'مقصد انتخاب شده' : editingPoint === 'to' || choosingDestination ? 'مقصد را انتخاب کنید' : editingPoint === 'from' ? 'مبدأ را تغییر دهید' : 'مبدأ انتخاب شد' }}</strong>
      <button v-if="editingPoint" type="button" class="button" :disabled="!selectedPoint" @click="finishPointEdit(true)">تأیید {{ editingLabel }}</button>
      <template v-else-if="selectedPoint && selectedDestination">
        <button type="button" class="button" @click="startTrip">مسیریابی</button>
        <button type="button" class="button" @click="swapEndpoints">جابجایی مبدا و مقصد</button>
      </template>
      <p v-else-if="choosingDestination">مقصد را روی نقشه انتخاب کنید یا آن را جست‌وجو کنید.</p>
      <template v-else>
        <button type="button" class="button" @click="choosingDestination = true">انتخاب مقصد</button>
        <button type="button" class="button" :disabled="locatingOrigin" @click="selectDestinationFromMyLocation">
          {{ locatingOrigin ? 'در حال یافتن موقعیت شما…' : 'انتخاب به عنوان مقصد از مکان فعلی' }}
        </button>
      </template>
      <RouterLink v-if="selectedStationId && !selectedDestination && !choosingDestination && !editingPoint" :to="{ name: 'specific-station', params: { stationId: selectedStationId } }" class="station-details">جزئیات ایستگاه</RouterLink>
      <button type="button" class="text-button" @click="cancelSelection">لغو</button>
    </section>
  <MapAlert class="map-caption" :message="loading && !snapshot ? 'در حال دریافت ایستگاه‌ها…' : failed ? 'دریافت ایستگاه‌ها ممکن نشد.' : ''"
    :role="failed ? 'alert' : 'status'">
    <button v-if="failed" class="text-button" type="button" @click="checkNetworkCache(true)">تلاش دوباره</button>
  </MapAlert>
  <p id="map-help" class="map-help">برای جزئیات، ایستگاه را لمس کنید. برای انتخاب مبدأ، روی نقشه لمس طولانی کنید یا جست‌وجو کنید. سپس انتخاب مقصد را بزنید و مقصد را روی نقشه یا با جست‌وجو انتخاب کنید.</p>
  </section>
</template>

<style scoped>
.metro-map { position: absolute; inset: 0; overflow: hidden; background: var(--surface); }
.metro-map-canvas { width: 100%; height: 100%; direction: ltr; }
.point-selection { position: absolute; z-index: 2; bottom: calc(112px + env(safe-area-inset-bottom, 0px)); left: 50%; transform: translateX(-50%); width: min(480px, calc(100% - 32px)); padding: 18px 20px; border: 1px solid var(--border); border-radius: 22px; background: var(--glass); box-shadow: 0 8px 30px #102c3420; display: grid; gap: 4px; }
.point-selection p { font-size: 13px; }
.point-selection .text-button { position: absolute; inset-inline-end: 16px; top: 4px; font-size: 12px; }
.point-selection strong { padding-inline-end: 32px; }
.station-details { font-size: 13px; color: var(--teal); text-align: center; padding: 6px; }
.map-notices { position: absolute; z-index: 1; top: 70px; right: calc(12px + env(safe-area-inset-right, 0px)); left: calc(50px + env(safe-area-inset-left, 0px)); display: grid; justify-items: start; gap: 6px; pointer-events: none; }
.map-message, .map-location-message { max-width: 100%; padding: 6px 12px; border-radius: 12px; background: var(--glass); color: var(--ink); pointer-events: auto; }
.location-settings { width: min(360px, calc(100% - 32px)); padding: 20px 24px; border: 1px solid var(--border); border-radius: 22px; background: var(--canvas); color: var(--ink); }
.location-settings::backdrop { background: #102c3466; }
.location-settings h2 { margin: 0 0 8px; }
.location-settings-actions { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; margin-top: 14px; }
.location-settings-actions .button { margin-top: 0; }
.map-toolbar {
  position: absolute;
  z-index: 3;
  top: 12px;
  right: calc(12px + env(safe-area-inset-right, 0px));
  left: calc(50px + env(safe-area-inset-left, 0px));
  display: flex;
  gap: 8px;
}
.map-filter { display: grid; place-items: center; flex: none; border: 1px solid var(--border); border-radius: 12px; width: 46px; height: 46px; background: var(--glass); color: var(--ink); }
.map-caption { position: absolute; bottom: calc(145px + env(safe-area-inset-bottom, 0px)); inset-inline: 16px; width: fit-content; margin-inline: auto; padding: 8px 14px; border-radius: 12px; background: var(--glass); color: var(--ink); font-size: 13px; }
.map-help { position: absolute; width: 1px; height: 1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
.metro-map :deep(.maplibregl-ctrl-top-left) { top: 0; left: env(safe-area-inset-left, 0px); }
.metro-map :deep(.maplibregl-ctrl-bottom-left), .metro-map :deep(.maplibregl-ctrl-bottom-right) { bottom: calc(112px + env(safe-area-inset-bottom, 0px)); }
.metro-map :deep(.maplibregl-ctrl button svg) { width: auto; height: auto; }
</style>
