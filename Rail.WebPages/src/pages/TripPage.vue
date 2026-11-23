<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import PageHeading from '../components/PageHeading.vue';
import JourneyMap from '../components/JourneyMap.vue';
import StreetLegDetails from '../components/StreetLegDetails.vue';
import JalaliDatePicker from '../components/JalaliDatePicker.vue';
import PersianTimePicker from '../components/PersianTimePicker.vue';
import { checkNetworkCache, useNetworkCache } from '../services/network';
import { mapTripSelection } from '../services/pageViewState';
import { useEndpointPlace } from '../services/endpointPlace';
import { searchStations, stationSearchWords } from '../services/stationSearch';
import { journeyChoices, selectedJourney } from '../services/journeyChoices';
import { tripDuration as duration } from '../services/tripDuration';
import { itineraryRailStyle } from '../services/itineraryStyle';
import { cachedTrip, cacheTrip, isTripFresh, rememberTripChoice } from '../services/tripCache';
import { apiBase } from '../services/api';
import { addStreetPaths, journeyGraph, intermediateStations, nearestStation, parseMapPoint } from '../services/journey';
import { gregorianToJalali } from '../services/jalaliDate';
import type { JourneyLeg, WalkingJourneyResult, TravelMode } from '../services/journey';

const route = useRoute();
const router = useRouter();
const optionsForm = ref<HTMLFormElement>();
const timeMode = ref('now');
const selectedDate = ref('');
const selectedTime = ref('');
const accessMode = ref<TravelMode>('walk');
const exitMode = ref<TravelMode>('walk');
const viaStationIds = ref<string[]>([]);
const viaSearch = ref('');
const modes: { value: TravelMode; label: string }[] = [
  { value: 'walk', label: 'پیاده‌روی' }, { value: 'car', label: 'خودرو / تاکسی' }, { value: 'motorcycle', label: 'موتورسیکلت' },
];
const modeLabel = (mode: string) => mode === 'car' ? 'سواری' : modes.find(item => item.value === mode)?.label ?? 'پیاده‌روی';
function syncControls() {
  const current = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Tehran', year: 'numeric', month: '2-digit',
    day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date()).replace(' ', 'T');
  const candidate = typeof route.query.dateTime === 'string' ? route.query.dateTime : current;
  const selected = gregorianToJalali(candidate.slice(0, 10)) ? candidate : current;
  selectedDate.value = selected.slice(0, 10); selectedTime.value = selected.slice(11, 16);
  timeMode.value = route.query.timeMode === 'arriveBy' ? 'arriveBy' : typeof route.query.dateTime === 'string' ? 'departAt' : 'now';
  accessMode.value = modes.find(mode => mode.value === route.query.accessMode)?.value ?? 'walk';
  exitMode.value = modes.find(mode => mode.value === route.query.exitMode)?.value ?? 'walk';
  const via = route.query.viaStationIds;
  viaStationIds.value = [...new Set((Array.isArray(via) ? via : [via]).filter((id): id is string => typeof id === 'string' && !!id))];
}
function planQuery() {
  const query = { ...route.query };
  delete query.transferMinutes;
  delete query.dateTime;
  delete query.viaStationIds;
  return { ...query, ...(timeMode.value === 'now' ? {} : { dateTime: `${selectedDate.value}T${selectedTime.value}` }), timeMode: timeMode.value === 'now' ? 'departAt' : timeMode.value,
    accessMode: accessMode.value, exitMode: exitMode.value,
    ...(viaStationIds.value.length ? { viaStationIds: viaStationIds.value } : {}) };
}
async function applyPlan() {
  const query = planQuery();
  const target = router.resolve({ query });
  if (target.fullPath === route.fullPath) await search();
  else await router.replace({ query });
}
function editEndpoint(point: 'from' | 'to') {
  if (optionsForm.value && !optionsForm.value.reportValidity()) return;
  void router.push({ name: 'map', query: { ...planQuery(), pick: point } });
}
const { snapshot } = useNetworkCache();
const viaOptions = computed(() => searchStations((snapshot.value?.data.stations ?? []).filter(station =>
  station.infrastructureStatus === 'operational' && station.lines.some(line => line.isOperational
    && snapshot.value?.data.routes.some(route => route.lineId === line.lineId
      && route.stops.some(stop => stop.stationId === station.id && stop.status === 'operational')))
  && !viaStationIds.value.includes(station.id)), stationSearchWords(viaSearch.value)).slice(0, 8));
function addVia(id: string) {
  if (viaStationIds.value.length < 8 && !viaStationIds.value.includes(id)) viaStationIds.value.push(id);
  viaSearch.value = '';
}
const from = computed(() => parseMapPoint(route.query.from));
const to = computed(() => parseMapPoint(route.query.to));
function swapEndpoints() {
  if (!from.value || !to.value || (optionsForm.value && !optionsForm.value.reportValidity())) return;
  void router.replace({ query: { ...planQuery(), from: to.value.join(','), to: from.value.join(',') } });
}

watch(() => route.query, query => {
  if (!from.value || !to.value) return;
  const previous = mapTripSelection.value;
  const fromKey = from.value.join(',');
  const toKey = to.value.join(',');
  const previousFrom = previous.from?.join(',');
  const previousTo = previous.to?.join(',');
  const reversed = fromKey !== toKey && previousFrom === toKey && previousTo === fromKey;
  mapTripSelection.value = {
    from: from.value, to: to.value, query: { ...query },
    stationId: reversed ? previous.destinationStationId : previousFrom === fromKey ? previous.stationId : undefined,
    destinationStationId: reversed ? previous.stationId : previousTo === toKey ? previous.destinationStationId : undefined,
  };
}, { immediate: true });
function pointPlace(point: [number, number] | undefined) {
  const station = point && snapshot.value ? nearestStation(snapshot.value.data, point) : undefined;
  return station ? `نزدیک ${station.nameFa}` : 'نقطه انتخابی روی نقشه';
}
const fromPlace = useEndpointPlace(from, computed(() => pointPlace(from.value)));
const toPlace = useEndpointPlace(to, computed(() => pointPlace(to.value)));
const responseResult = ref<WalkingJourneyResult>();
const selectedChoiceId = ref<string>();
const choices = computed(() => responseResult.value ? journeyChoices(responseResult.value) : []);
const result = computed(() => responseResult.value ? selectedJourney(responseResult.value, selectedChoiceId.value) : undefined);
const finalLeg = computed(() => result.value?.legs[result.value.legs.length - 1]);
const arrivalRailStyle = computed(() => itineraryRailStyle('#000000', finalLeg.value
  ? snapshot.value?.data.lines.find(line => line.id === finalLeg.value!.lineId)?.color ?? '#000000' : undefined));
const origin = computed(() => snapshot.value?.data.stations.find(station => station.id === result.value?.access?.stationId));
const destination = computed(() => snapshot.value?.data.stations.find(station => station.id === result.value?.exit?.stationId));
const loading = ref(false);
const error = ref('');
let request: AbortController | undefined;
let refreshTimer: ReturnType<typeof setInterval> | undefined;
const graph = computed(() => {
  if (!snapshot.value || !result.value) return;
  try {
    const data = journeyGraph(snapshot.value.data, result.value.legs, true);
    const paths = result.value.walkOnly ? [result.value.directPath] : [result.value.access?.path, result.value.exit?.path];
    return addStreetPaths(data, paths);
  } catch { return; }
});
const stationName = (id: string) => snapshot.value?.data.stations.find(station => station.id === id)?.nameFa ?? id;
function stationBadgeStyle(id: string, legs: JourneyLeg[]) {
  return lineBadgeStyle([...new Set(legs.filter(leg => leg.fromStationId === id || leg.toStationId === id)
    .map(leg => leg.lineId))].sort((a, b) => a - b));
}
function lineBadgeStyle(lineIds: number[]) {
  const network = snapshot.value?.data;
  const colors = [...new Set(lineIds.map(id => network?.lines.find(line => line.id === id)?.color)
    .map(color => color && /^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(color) ? color : '#00636a'))];
  if (!colors.length) colors.push('#00636a');
  const stops = colors.flatMap((color, index) => [
    `${color} ${(index + .1) / colors.length * 100}%`, `${color} ${(index + .9) / colors.length * 100}%`,
  ]);
  return { '--station-border': `linear-gradient(90deg, ${stops.join(', ')})` };
}
const lineName = (id: number) => snapshot.value?.data.lines.find(line => line.id === id)?.nameFa ?? `خط ${id}`;
function legRailStyle(index: number) {
  const legs = result.value?.legs ?? [];
  const leg = legs[index], previous = legs[index - 1];
  const color = (id: number) => snapshot.value?.data.lines.find(line => line.id === id)?.color ?? '#000000';
  if (!leg) return itineraryRailStyle('#000000');
  const transfer = previous && previous.toStationId === leg.fromStationId && previous.lineId !== leg.lineId;
  const incoming = transfer ? color(previous.lineId) : index === 0 && result.value?.access ? '#000000' : undefined;
  return itineraryRailStyle(color(leg.lineId), incoming);
}
const timeLabel = (value: string) => value.slice(11, 16).replace(/\d/g, digit => '۰۱۲۳۴۵۶۷۸۹'[Number(digit)]!);
const clockLabel = (value: string) => value.slice(11, 16).replace(/\d/g, digit => '۰۱۲۳۴۵۶۷۸۹'[Number(digit)]!);
function dateLabel(value: string) {
  return new Intl.DateTimeFormat('fa-IR', { timeZone: 'Asia/Tehran', year: 'numeric', month: 'long', day: 'numeric' })
    .format(new Date(`${value}+03:30`));
}
const hasMissingStreetPath = computed(() => {
  if (!result.value) return false;
  const paths = result.value.walkOnly ? [result.value.directPath] : [result.value.access?.path, result.value.exit?.path];
  return paths.some(path => path?.source !== 'graphhopper');
});
const localTime = (value: string) => Date.parse(`${value}+03:30`);
function waitStart(index: number) {
  if (!result.value) return 0;
  return index === 0
    ? localTime(result.value.departureLocal) + (result.value.access?.seconds ?? 0) * 1000
    : localTime(result.value.legs[index - 1]!.arrivalLocal)
      + (result.value.legs[index]!.transfer?.minimumSeconds ?? 0) * 1000;
}
function waitClock(index: number) {
  return new Intl.DateTimeFormat('fa-IR', { timeZone: 'Asia/Tehran', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
    .format(new Date(waitStart(index)));
}
async function search(background = false) {
  if (background && request) return;
  request?.abort();
  const current = new AbortController();
  const cacheKey = route.fullPath;
  request = current;
  if (!background) { responseResult.value = undefined; selectedChoiceId.value = undefined; }
  error.value = '';
  loading.value = false;
  if (!from.value || !to.value) { error.value = 'مبدأ و مقصد را روی نقشه انتخاب کنید.'; request = undefined; return; }
  loading.value = !background;
  let timeout: ReturnType<typeof setTimeout> | undefined;
  try {
    await checkNetworkCache();
    if (current.signal.aborted) return;
    const query = new URLSearchParams({ fromLongitude: String(from.value[0]), fromLatitude: String(from.value[1]),
      toLongitude: String(to.value[0]), toLatitude: String(to.value[1]) });
    for (const key of ['dateTime', 'timeMode', 'accessMode', 'exitMode']) {
      const value = route.query[key]; if (typeof value === 'string') query.set(key, value);
    }
    const via = route.query.viaStationIds;
    for (const id of Array.isArray(via) ? via : [via]) if (typeof id === 'string' && id) query.append('viaStationIds', id);
    timeout = setTimeout(() => current.abort(), 60_000);
    const response = await fetch(`${apiBase}/api/navigation/walking-journeys?${query}`, { signal: current.signal, cache: 'no-store', headers: { Accept: 'application/json' } });
    if (response.status === 404) throw new Error('no-route');
    if (!response.ok) throw new Error('journey');
    const data: WalkingJourneyResult = await response.json();
    if (!current.signal.aborted) {
      responseResult.value = data;
      selectedChoiceId.value = selectedJourney(data, selectedChoiceId.value).id;
      cacheTrip(cacheKey, data, selectedChoiceId.value);
    }
  } catch (failure) {
    if (request === current) error.value = failure instanceof Error && failure.message === 'no-route'
      ? 'مسیر مناسبی با زمان و ایستگاه‌های انتخابی پیدا نشد. زمان یا ایستگاه‌های الزامی را تغییر دهید.'
      : 'دریافت مسیر ممکن نشد. اتصال اینترنت را بررسی کنید و دوباره تلاش کنید.';
  } finally {
    clearTimeout(timeout);
    if (request === current) { loading.value = false; request = undefined; }
  }
}
watch(selectedChoiceId, id => {
  if (route.name === 'new-trip') rememberTripChoice(route.fullPath, id);
});
watch(() => route.fullPath, () => {
  if (route.name !== 'new-trip') return;
  syncControls();
  const saved = cachedTrip(route.fullPath);
  if (!saved) { void search(); return; }
  request?.abort(); request = undefined;
  responseResult.value = saved.response;
  selectedChoiceId.value = selectedJourney(saved.response, saved.selectedChoiceId).id;
  loading.value = false; error.value = '';
  if (!isTripFresh(route.fullPath)) void search(true);
}, { immediate: true });
function refresh() {
  if (route.name === 'new-trip' && document.visibilityState === 'visible' && !isTripFresh(route.fullPath)) void search(true);
}
onMounted(() => {
  refreshTimer = setInterval(refresh, 30_000);
  document.addEventListener('visibilitychange', refresh);
});
onBeforeUnmount(() => {
  request?.abort(); request = undefined;
  clearInterval(refreshTimer);
  document.removeEventListener('visibilitychange', refresh);
});
</script>

<template>
  <PageHeading title="سفر جدید" description="مقایسه مسیرها با پیاده‌روی، انتظار قطار و تعویض خط" />
  <details class="card trip-plan">
    <summary class="plan-summary">
      <span class="plan-heading"><strong>برنامه سفر</strong>
        <span class="plan-open-hint">نمایش و تغییر زمان، مسیر و ایستگاه‌ها</span>
        <span class="plan-close-hint">بستن تنظیمات سفر</span>
      </span>
      <span class="plan-toggle" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="m6 9 6 6 6-6" /></svg></span>
    </summary>
    <form ref="optionsForm" class="trip-options" @submit.prevent="applyPlan">
    <fieldset class="time-choice">
      <legend class="visually-hidden">زمان موردنظر سفر</legend>
      <label><input v-model="timeMode" type="radio" name="trip-time-mode" value="now" />همین حالا</label>
      <label><input v-model="timeMode" type="radio" name="trip-time-mode" value="departAt" />شروع در</label>
      <label><input v-model="timeMode" type="radio" name="trip-time-mode" value="arriveBy" />پایان در</label>
    </fieldset>
    <div class="options-row">
      <label v-if="timeMode !== 'now'">تاریخ شمسی<JalaliDatePicker v-model="selectedDate" /></label>
      <div v-if="timeMode !== 'now'" class="option-field"><span>ساعت و دقیقه</span><PersianTimePicker v-model="selectedTime" /></div>
    </div>
    <div class="options-row">
      <label>از مبدأ تا اولین ایستگاه<select v-model="accessMode"><option v-for="mode in modes" :key="mode.value" :value="mode.value">{{ mode.label }}</option></select></label>
      <label>از آخرین ایستگاه تا مقصد<select v-model="exitMode"><option v-for="mode in modes" :key="mode.value" :value="mode.value">{{ mode.label }}</option></select></label>
    </div>
    <div class="endpoint-buttons">
      <button type="button" :aria-label="`تغییر مبدأ، ${fromPlace}`" @click="editEndpoint('from')">
        <span class="endpoint-label">مبدأ <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m15 5 4 4M4 20l4-1L20 7a2.8 2.8 0 0 0-4-4L4 15Z" /></svg></span>
        <strong>{{ fromPlace }}</strong><span class="endpoint-action">تغییر مبدأ روی نقشه</span>
      </button>
      <button type="button" :aria-label="`تغییر مقصد، ${toPlace}`" @click="editEndpoint('to')">
        <span class="endpoint-label">مقصد <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m15 5 4 4M4 20l4-1L20 7a2.8 2.8 0 0 0-4-4L4 15Z" /></svg></span>
        <strong>{{ toPlace }}</strong><span class="endpoint-action">تغییر مقصد روی نقشه</span>
      </button>
    </div>
    <button v-if="from && to" class="button" type="button" @click="swapEndpoints">جابجایی مبدا و مقصد</button>
    <fieldset class="via-stations">
      <legend>عبور از ایستگاه‌ها <span>(اختیاری)</span></legend>
      <p>مسیر باید از همه ایستگاه‌های انتخابی عبور کند؛ ترتیب عبور با مسیر تعیین می‌شود. حداکثر ۸ ایستگاه.</p>
      <ul v-if="viaStationIds.length" class="via-selected">
        <li v-for="id in viaStationIds" :key="id">{{ stationName(id) }}
          <button type="button" :aria-label="`حذف ${stationName(id)}`" @click="viaStationIds = viaStationIds.filter(item => item !== id)">×</button>
        </li>
      </ul>
      <p v-else class="via-empty">ایستگاهی انتخاب نشده؛ مسیر آزاد است.</p>
      <template v-if="viaStationIds.length < 8">
        <label class="via-search">افزودن ایستگاه<input v-model="viaSearch" type="search" placeholder="جستجوی نام ایستگاه" autocomplete="off" /></label>
        <ul v-if="viaSearch.trim()" class="via-results">
          <li v-for="station in viaOptions" :key="station.id"><button type="button" @click="addVia(station.id)"><span>{{ station.nameFa }}</span><span>＋ افزودن</span></button></li>
          <li v-if="!viaOptions.length">ایستگاهی پیدا نشد.</li>
        </ul>
      </template>
    </fieldset>
    <button class="button" type="submit" :disabled="loading">محاسبه مسیر</button>
    </form>
  </details>
  <details v-if="result" class="card trip-plan trip-choices">
    <summary class="plan-summary">
      <span class="plan-heading">
        <span class="choice-heading-row"><strong>سفر انتخابی</strong>
          <span class="trip-time-boxes">
            <span class="trip-time-box"><span>شروع</span><time :datetime="result.departureLocal" :title="dateLabel(result.departureLocal)" dir="ltr">{{ clockLabel(result.departureLocal) }}</time></span>
            <span class="trip-time-box"><span>پایان</span><time :datetime="result.arrivalLocal" :title="dateLabel(result.arrivalLocal)" dir="ltr">{{ clockLabel(result.arrivalLocal) }}</time></span>
          </span>
        </span>
        <span class="selected-stations" v-if="origin && destination">
          <span class="station-badge" :style="stationBadgeStyle(origin.id, result.legs)">{{ origin.nameFa }}</span>
          <span>←</span>
          <span class="station-badge" :style="stationBadgeStyle(destination.id, result.legs)">{{ destination.nameFa }}</span>
        </span>
        <span v-else class="selected-stations">پیاده‌روی تا مقصد</span>
        <span class="choice-duration">{{ duration(result.totalSeconds) }}</span>
      </span>
      <span class="plan-toggle" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="m6 9 6 6 6-6" /></svg></span>
    </summary>
    <fieldset class="journey-choices">
      <legend class="visually-hidden">انتخاب مسیر بر اساس زمان کل سفر</legend>
      <label v-for="(choice, index) in choices" :key="choice.id" class="journey-choice" :class="{ selected: result.id === choice.id }">
        <input type="radio" name="journey-choice" :value="choice.id" :checked="result.id === choice.id" @change="selectedChoiceId = choice.id" />
        <span class="choice-stations" v-if="choice.access && choice.exit">
          <span class="station-badge" :style="stationBadgeStyle(choice.access.stationId, choice.legs)">{{ stationName(choice.access.stationId) }}</span>
          <span>←</span>
          <span class="station-badge" :style="stationBadgeStyle(choice.exit.stationId, choice.legs)">{{ stationName(choice.exit.stationId) }}</span>
        </span>
        <span class="choice-stations" v-else>پیاده‌روی تا مقصد</span>
        <strong class="choice-duration">{{ duration(choice.totalSeconds) }}</strong>
        <span v-if="index === 0" class="choice-fastest">کوتاه‌ترین سفر</span>
      </label>
    </fieldset>
    <p v-if="choices.length < 4" class="map-note">{{ (choices.length - 1).toLocaleString('fa-IR') }} مسیر جایگزین برای زمان و شرایط انتخابی در دسترس است.</p>
  </details>
  <p v-if="loading" class="card" role="status">در حال یافتن بهترین مسیر…</p>
  <section v-if="error" class="card" role="alert">
    <p>{{ error }} {{ result ? 'اطلاعات مربوط به آخرین دریافت موفق است.' : '' }}</p><button class="text-button" @click="search()">تلاش دوباره</button>
  </section>
  <section v-if="result?.walkOnly" class="card" role="status">
    <JourneyMap :key="result.id" v-if="graph && from && to" :graph="graph" :from="from" :to="to" />
    <h2>پیشنهاد: پیاده‌روی تا مقصد</h2>
    <p>پیاده‌روی مستقیم در زمان انتخابی مناسب‌تر است یا قطار مناسبی در بازه جست‌وجو پیدا نشد.</p>
    <p>{{ duration(result.directWalkSeconds) }} · {{ result.directWalkMeters.toLocaleString('fa-IR') }} متر</p>
    <StreetLegDetails v-if="result.directPath" :path="result.directPath" />
  </section>
  <template v-else-if="result?.legs.length">
    <JourneyMap :key="result.id" v-if="graph && from && to" :graph="graph" :from="from" :to="to" />
    <p v-else role="status">مختصات این مسیر برای نمایش روی نقشه کامل نیست.</p>
    <section class="card itinerary" aria-label="مراحل سفر">
      <h2>مسیر پیشنهادی</h2>
      <p v-if="result.viaStationIds?.length">عبور الزامی:
        <template v-for="(id, index) in result.viaStationIds" :key="id"><span v-if="index">، </span><RouterLink class="station-text-link" :to="{ name: 'specific-station', params: { stationId: id } }">{{ stationName(id) }}</RouterLink></template>
      </p>
      <ol>
        <li v-if="result.access" :style="itineraryRailStyle('#000000')"><h3>{{ modeLabel(result.accessMode) }}</h3>
          <p>{{ duration(result.access.seconds) }} · {{ result.access.distanceMeters.toLocaleString('fa-IR') }} متر</p>
          <StreetLegDetails v-if="result.access.path" :path="result.access.path" />
        </li>
        <li v-for="(leg, index) in result.legs" :key="index" :style="legRailStyle(index)">
          <h3><RouterLink class="station-badge" :style="stationBadgeStyle(leg.fromStationId, result.legs)" :to="{ name: 'specific-station', params: { stationId: leg.fromStationId } }" @click.stop>{{ stationName(leg.fromStationId) }}</RouterLink></h3>
          <p>{{ lineName(leg.lineId) }} - به سمت <RouterLink class="station-text-link" :to="{ name: 'specific-station', params: { stationId: leg.directionStationId } }">{{ stationName(leg.directionStationId) }}</RouterLink></p>
          <p>ورود به ایستگاه: {{ waitClock(index) }}</p>
          <p>رسیدن قطار به سکو: {{ timeLabel(leg.boardingArrivalLocal ?? leg.departureLocal) }}</p>
          <p>رسیدن قطار به مقصد: {{ timeLabel(leg.arrivalLocal) }}</p>
          <p>مدت {{ duration((localTime(leg.arrivalLocal) - localTime(leg.departureLocal)) / 1000) }}</p>
          <details class="intermediate-stations">
            <summary>ایستگاه‌های مابین ({{ intermediateStations(snapshot?.data, leg).length.toLocaleString('fa-IR') }})</summary>
            <ol>
              <li v-for="station in intermediateStations(snapshot?.data, leg)" :key="station.id">
                <RouterLink class="station-badge" :style="lineBadgeStyle([leg.lineId])" :to="{ name: 'specific-station', params: { stationId: station.id } }" @click.stop>{{ station.nameFa }}</RouterLink>
              </li>
            </ol>
            <p v-if="!intermediateStations(snapshot?.data, leg).length">ایستگاه میانی برای نمایش موجود نیست.</p>
          </details>
        </li>
        <li v-if="finalLeg" :style="arrivalRailStyle">
          <h3><RouterLink class="station-badge" :style="stationBadgeStyle(finalLeg.toStationId, [finalLeg])" :to="{ name: 'specific-station', params: { stationId: finalLeg.toStationId } }" @click.stop>{{ stationName(finalLeg.toStationId) }}</RouterLink></h3>
          <p>پیاده شدن از قطار · {{ clockLabel(finalLeg.arrivalLocal) }}</p>
        </li>
        <li :style="itineraryRailStyle('#000000')"><h3>{{ modeLabel(result.exitMode) }}</h3><p>{{ duration(result.exit?.seconds ?? 0) }} · {{ result.exit?.distanceMeters.toLocaleString('fa-IR') }} متر</p>
          <StreetLegDetails v-if="result.exit?.path" :path="result.exit.path" />
        </li>
      </ol>
    </section>
  </template>
  <footer v-if="result" class="trip-disclaimer" aria-label="توضیحات سفر">
    <p class="map-note">دایره‌های آبی: پیاده‌روی · مشکی: مسیر خیابانی خودرو یا موتور · رنگ خطوط مترو: بخش ریلی (نمای شماتیک بین ایستگاه‌ها).</p>
  <p v-if="hasMissingStreetPath" class="card route-warning" role="status">مسیر خیابانی برخی بخش‌ها دریافت نشد؛ زمان آن‌ها تخمینی است و مسیر ناموجود روی نقشه رسم نشده است.</p>
    <p v-if="result.access || result.exit" class="map-note">زمان دسترسی شامل ۲ دقیقه رسیدن به سکو و زمان خروج شامل ۲ دقیقه خروج از ایستگاه است.</p>
  <p class="map-note" role="status">ساعت‌ها به وقت تهران هستند. زمان تعویض خط: ۲ دقیقه.
    زمان بخش خیابانی تخمینی است و ترافیک زنده یا پیش‌بینی‌شده را شامل نمی‌شود. زمان قطارها از برنامه پایگاه داده است.
    هندسه دقیق ریل و راهروهای داخل ایستگاه در داده‌ها موجود نیست. آخرین دریافت {{ timeLabel(result.requestedAtLocal) }} · به‌روزرسانی هر ۳۰ ثانیه.</p>
  </footer>
</template>

<style scoped>
.intermediate-stations { margin-block: 8px; font-size: 13px; }
.intermediate-stations summary { color: var(--teal); cursor: pointer; min-height: 36px; padding-block: 4px; }
.intermediate-stations ol { list-style: none; display: grid; justify-items: start; gap: 8px; padding: 4px 0; margin: 0; }
.trip-disclaimer { margin-top: 24px; padding-top: 16px; border-top: 1px solid var(--border); }
.trip-choices > .plan-summary { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 8px 12px; }
.trip-choices .plan-heading { display: contents; }
.trip-choices .selected-stations, .trip-choices .plan-heading > a.station-badge { cursor: pointer; }
a.station-badge:hover { text-decoration: underline; }
a.station-badge:focus-visible, .station-text-link:focus-visible { outline: 2px solid var(--teal); outline-offset: 3px; }
.station-text-link { color: inherit; text-decoration: underline; text-underline-offset: 3px; }
.choice-duration { grid-column: 1; }
.trip-choices .plan-toggle { grid-column: 2; grid-row: 2 / span 2; }
.choice-heading-row { grid-column: 1 / -1; display: flex; align-items: center; justify-content: space-between; flex-wrap: nowrap; gap: 8px; min-width: 0; }
.choice-heading-row > strong { white-space: nowrap; }
.trip-time-boxes { display: flex; flex: none; gap: 8px; }
.trip-time-box { display: grid; justify-items: center; gap: 2px; min-width: 54px; padding: 5px 8px; border: 1px solid var(--border); border-radius: 10px; background: var(--active); }
.trip-time-box > span { font-size: 11px; color: var(--muted); }
.trip-time-box time { font-size: 16px; font-weight: 700; color: var(--ink); font-variant-numeric: tabular-nums; }
.journey-choices { display: grid; gap: 10px; border: 0; margin: 18px 0 0; padding: 0; min-width: 0; }
.journey-choice { display: grid; grid-template-columns: auto 1fr; align-items: center; column-gap: 12px; row-gap: 6px; padding: 14px; border: 1px solid var(--border); border-radius: 14px; cursor: pointer; }
.journey-choice:hover, .journey-choice.selected { border-color: var(--teal); background: var(--active); }
.journey-choice input { grid-row: span 3; width: 20px; height: 20px; accent-color: var(--teal); }
.choice-stations, .selected-stations { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; font-weight: 600; overflow-wrap: anywhere; }
.station-badge { color: inherit; text-decoration: none; display: inline-block; max-width: 100%; padding: 3px 8px; border: 2px solid transparent; border-radius: 7px; background: linear-gradient(var(--surface), var(--surface)) padding-box, var(--station-border) border-box; line-height: 1.6; }
.choice-duration { color: var(--teal); font-size: 13px; }
.choice-fastest { color: var(--muted); font-size: 12px; }
.trip-plan { margin-block: 12px 20px; }
.plan-summary { display: flex; align-items: center; justify-content: space-between; gap: 16px; cursor: pointer; list-style: none; border-radius: 10px; }
.plan-summary::-webkit-details-marker { display: none; }
.plan-summary:focus-visible { outline: 3px solid var(--teal); outline-offset: 6px; }
.plan-heading { display: grid; gap: 5px; min-width: 0; }
.plan-heading strong { font-size: 20px; }
.plan-open-hint, .plan-close-hint { color: var(--teal); font-size: 13px; }
.plan-toggle { display: grid; place-items: center; flex: none; width: 44px; height: 44px; border: 1px solid var(--teal); border-radius: 12px; background: var(--active); color: var(--teal); }
.plan-toggle svg { width: 24px; height: 24px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
.plan-close-hint, .trip-plan[open] .plan-open-hint { display: none; }
.trip-plan[open] .plan-close-hint { display: inline; }
.trip-plan[open] .plan-toggle svg { transform: rotate(180deg); }
.trip-options { margin-top: 18px; padding-top: 6px; border-top: 1px solid var(--border); }
.trip-options > .button { width: 100%; }
.options-row, .endpoint-buttons { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; margin-block: 12px; }
.options-row > label, .options-row > .option-field { display: grid; min-width: 0; gap: 6px; font-size: 13px; }
.options-row input, .options-row select { width: 100%; min-width: 0; min-height: 46px; border: 1px solid var(--border); border-radius: 12px; padding: 8px; color: var(--ink); background: var(--canvas); }
.options-row input { direction: ltr; }
.time-choice { display: flex; flex-wrap: wrap; gap: 24px; border: 0; padding: 0; margin: 12px 0; }
.time-choice label { display: inline-flex; align-items: center; gap: 8px; min-height: 44px; white-space: nowrap; cursor: pointer; }
.time-choice input { width: 18px; height: 18px; margin: 0; accent-color: var(--teal); }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
.endpoint-buttons button { display: grid; gap: 4px; min-width: 0; padding: 12px; border: 2px solid var(--teal); border-radius: 14px; text-align: start; background: var(--active); color: var(--ink); }
.endpoint-buttons button { cursor: pointer; }
.endpoint-buttons button:hover { box-shadow: 0 0 0 3px #00636a20; }
.endpoint-buttons button:focus-visible { outline: 3px solid var(--teal); outline-offset: 3px; }
.endpoint-label { display: flex; align-items: center; justify-content: space-between; color: var(--teal); font-size: 12px; }
.endpoint-label svg { width: 20px; height: 20px; fill: none; stroke: currentColor; stroke-width: 1.8; }
.endpoint-action { color: var(--teal); font-size: 12px; text-decoration: underline; margin-top: 6px; }
.via-stations { border: 1px solid var(--border); border-radius: 14px; padding: 14px; margin-block: 18px; min-width: 0; }
.via-stations legend { padding-inline: 6px; font-weight: 700; }
.via-stations legend span, .via-stations p { font-size: 12px; color: var(--muted); }
.via-search { display: grid; gap: 6px; font-size: 13px; }
.via-search input { min-width: 0; width: 100%; padding: 12px; border: 1px solid var(--border); border-radius: 10px; background: var(--canvas); color: var(--ink); }
.via-selected, .via-results { list-style: none; padding: 0; }
.via-selected { display: flex; flex-wrap: wrap; gap: 8px; }
.via-selected li { display: flex; align-items: center; gap: 6px; border-radius: 12px; padding-inline-start: 12px; background: var(--active); font-size: 13px; }
.via-selected button { min-width: 40px; min-height: 40px; border: 0; background: transparent; color: var(--teal); font-size: 22px; }
.via-results button { display: flex; align-items: center; justify-content: space-between; gap: 12px; width: 100%; min-height: 44px; border: 0; border-bottom: 1px solid var(--border); padding: 10px; background: transparent; color: var(--ink); text-align: start; }
.via-results button:hover { background: var(--active); }
.endpoint-buttons strong { font-size: 13px; overflow-wrap: anywhere; }
.trip-options > p, .route-warning { font-size: 13px; }
@media (max-width: 400px) { .trip-plan { padding: 16px; } .options-row, .endpoint-buttons { gap: 8px; } .time-choice { gap: 20px; } }
.endpoints strong { color: var(--ink); }
.endpoints small, .map-note { color: var(--muted); font-size: 12px; }
.map-note { margin-block: 8px 20px; }
.itinerary { margin-top: 20px; }
.itinerary > ol { list-style: none; padding: 0; margin-bottom: 0; }
.itinerary > ol > li { position: relative; padding-block-end: 22px; padding-inline-start: 32px; margin-inline-start: 2px; }
.itinerary > ol > li::before { content: ''; position: absolute; z-index: 1; inset-inline-start: -9px; top: 7px; box-sizing: border-box; width: 21px; height: 21px; border-radius: 50%; background: var(--step-dot, #000000); }
.itinerary > ol > li:not(:last-child)::after { content: ''; position: absolute; inset-inline-start: 0; top: 17.5px; bottom: -17.5px; width: 3px; background: var(--step-line, #000000); }
.itinerary > ol > li:last-child { padding-bottom: 0; }
h3 { margin: 0; font-size: 18px; }
</style>
