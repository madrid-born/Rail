<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import PageHeading from '../components/PageHeading.vue';
import JourneyMap from '../components/JourneyMap.vue';
import StreetLegDetails from '../components/StreetLegDetails.vue';
import JalaliDatePicker from '../components/JalaliDatePicker.vue';
import { checkNetworkCache, useNetworkCache } from '../services/network';
import { apiBase } from '../services/api';
import { journeyGraph, nearestStation, parseMapPoint } from '../services/journey';
import { gregorianToJalali } from '../services/jalaliDate';
import type { JourneyLeg, WalkingJourneyResult, TravelMode } from '../services/journey';

const route = useRoute();
const router = useRouter();
const optionsForm = ref<HTMLFormElement>();
const timeMode = ref('departAt');
const selectedDate = ref('');
const selectedTime = ref('');
const accessMode = ref<TravelMode>('walk');
const exitMode = ref<TravelMode>('walk');
const modes: { value: TravelMode; label: string }[] = [
  { value: 'walk', label: 'پیاده‌روی' }, { value: 'car', label: 'خودرو / تاکسی' }, { value: 'motorcycle', label: 'موتورسیکلت' },
];
const modeLabel = (mode: string) => modes.find(item => item.value === mode)?.label ?? 'پیاده‌روی';
function syncControls() {
  const current = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Tehran', year: 'numeric', month: '2-digit',
    day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date()).replace(' ', 'T');
  const candidate = typeof route.query.dateTime === 'string' ? route.query.dateTime : current;
  const selected = gregorianToJalali(candidate.slice(0, 10)) ? candidate : current;
  selectedDate.value = selected.slice(0, 10); selectedTime.value = selected.slice(11, 16);
  timeMode.value = route.query.timeMode === 'arriveBy' ? 'arriveBy' : 'departAt';
  accessMode.value = modes.find(mode => mode.value === route.query.accessMode)?.value ?? 'walk';
  exitMode.value = modes.find(mode => mode.value === route.query.exitMode)?.value ?? 'walk';
}
function planQuery() {
  const query = { ...route.query };
  delete query.transferMinutes;
  return { ...query, dateTime: `${selectedDate.value}T${selectedTime.value}`, timeMode: timeMode.value,
    accessMode: accessMode.value, exitMode: exitMode.value };
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
const from = computed(() => parseMapPoint(route.query.from));
const to = computed(() => parseMapPoint(route.query.to));
function pointPlace(point: [number, number] | undefined) {
  const station = point && snapshot.value ? nearestStation(snapshot.value.data, point) : undefined;
  return station ? `نزدیک ${station.nameFa}` : 'نقطه انتخابی روی نقشه';
}
const result = ref<WalkingJourneyResult>();
const origin = computed(() => snapshot.value?.data.stations.find(station => station.id === result.value?.access?.stationId));
const destination = computed(() => snapshot.value?.data.stations.find(station => station.id === result.value?.exit?.stationId));
const loading = ref(false);
const error = ref('');
let request: AbortController | undefined;
let refreshTimer: ReturnType<typeof setInterval> | undefined;
const now = ref(Date.now());
let clockTimer: ReturnType<typeof setInterval> | undefined;
const graph = computed(() => {
  if (!snapshot.value || !result.value) return;
  try {
    const data = journeyGraph(snapshot.value.data, result.value.legs);
    const paths = result.value.walkOnly ? [result.value.directPath] : [result.value.access?.path, result.value.exit?.path];
    for (const path of paths) {
      if (path?.source === 'neshan' && path.coordinates.length > 1) data.features.push({ type: 'Feature',
        geometry: { type: 'LineString', coordinates: path.coordinates },
        properties: { street: true, walking: path.mode === 'walk', color: path.mode === 'walk' ? '#58716f' : '#bc6727' } });
    }
    return data;
  } catch { return; }
});
const stationName = (id: string) => snapshot.value?.data.stations.find(station => station.id === id)?.nameFa ?? id;
const lineName = (id: number) => snapshot.value?.data.lines.find(line => line.id === id)?.nameFa ?? `خط ${id}`;
const timeLabel = (value: string) => value.slice(11, 19).replace(/\d/g, digit => '۰۱۲۳۴۵۶۷۸۹'[Number(digit)]!);
const minutes = (seconds: number) => Math.max(0, Math.ceil(seconds / 60)).toLocaleString('fa-IR');
function duration(seconds: number) {
  const total = Math.max(0, Math.round(seconds));
  return `${Math.floor(total / 60).toLocaleString('fa-IR')} دقیقه و ${(total % 60).toLocaleString('fa-IR')} ثانیه`;
}
function dateLabel(value: string) {
  return new Intl.DateTimeFormat('fa-IR', { timeZone: 'Asia/Tehran', year: 'numeric', month: 'long', day: 'numeric' })
    .format(new Date(`${value}+03:30`));
}
const hasMissingStreetPath = computed(() => {
  if (!result.value) return false;
  const paths = result.value.walkOnly ? [result.value.directPath] : [result.value.access?.path, result.value.exit?.path];
  return paths.some(path => path?.source !== 'neshan');
});
const localTime = (value: string) => Date.parse(`${value}+03:30`);
function countdown(value: string) { return minutes((localTime(value) - now.value) / 1000); }
function trainLocation(leg: JourneyLeg) {
  const position = leg.trainPosition;
  if (position?.status === 'at_station') return `در ایستگاه ${stationName(position.stationId!)}`;
  if (position?.status === 'between_stations') return `بین ${stationName(position.fromStationId!)} و ${stationName(position.toStationId!)}`;
  return 'هنوز حرکت نکرده است';
}
function waitMinutes(index: number) {
  if (!result.value) return '';
  const leg = result.value.legs[index]!;
  const ready = index === 0
    ? localTime(result.value.departureLocal) + (result.value.access?.seconds ?? 0) * 1000
    : localTime(result.value.legs[index - 1]!.arrivalLocal) + (leg.transfer?.minimumSeconds ?? 0) * 1000;
  return duration((localTime(leg.departureLocal) - ready) / 1000);
}
async function search(background = false) {
  if (background && request) return;
  request?.abort();
  const current = new AbortController();
  request = current;
  if (!background) result.value = undefined;
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
    timeout = setTimeout(() => current.abort(), 60_000);
    const response = await fetch(`${apiBase}/api/navigation/walking-journeys?${query}`, { signal: current.signal, cache: 'no-store', headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error('journey');
    const data: WalkingJourneyResult = await response.json();
    if (!current.signal.aborted) result.value = data;
  } catch {
    if (request === current) error.value = 'دریافت مسیر ممکن نشد. اتصال اینترنت را بررسی کنید و دوباره تلاش کنید.';
  } finally {
    clearTimeout(timeout);
    if (request === current) { loading.value = false; request = undefined; }
  }
}
watch(() => route.fullPath, () => { syncControls(); void search(); }, { immediate: true });
function refresh() { if (document.visibilityState === 'visible') void search(true); }
onMounted(() => {
  refreshTimer = setInterval(refresh, 30_000);
  clockTimer = setInterval(() => { now.value = Date.now(); }, 1000);
  document.addEventListener('visibilitychange', refresh);
});
onBeforeUnmount(() => {
  request?.abort(); request = undefined;
  clearInterval(refreshTimer); clearInterval(clockTimer);
  document.removeEventListener('visibilitychange', refresh);
});
</script>

<template>
  <PageHeading title="سفر جدید" description="مقایسه مسیرها با پیاده‌روی، انتظار قطار و تعویض خط" />
  <RouterLink class="text-button" to="/map">بازگشت به نقشه و انتخاب نقاط جدید</RouterLink>
  <form ref="optionsForm" class="card trip-options" @submit.prevent="applyPlan">
    <h2>برنامه سفر</h2>
    <fieldset class="time-choice">
      <legend class="visually-hidden">زمان موردنظر سفر</legend>
      <label><input v-model="timeMode" type="radio" name="trip-time-mode" value="departAt" />شروع در</label>
      <label><input v-model="timeMode" type="radio" name="trip-time-mode" value="arriveBy" />پایان در</label>
    </fieldset>
    <div class="options-row">
      <label>تاریخ شمسی<JalaliDatePicker v-model="selectedDate" /></label>
      <label>ساعت و دقیقه<input v-model="selectedTime" type="time" required step="60" /></label>
    </div>
    <div class="options-row">
      <label>از مبدأ تا اولین ایستگاه<select v-model="accessMode"><option v-for="mode in modes" :key="mode.value" :value="mode.value">{{ mode.label }}</option></select></label>
      <label>از آخرین ایستگاه تا مقصد<select v-model="exitMode"><option v-for="mode in modes" :key="mode.value" :value="mode.value">{{ mode.label }}</option></select></label>
    </div>
    <div class="endpoint-buttons">
      <button type="button" :aria-label="`تغییر مبدأ، ${pointPlace(from)}`" @click="editEndpoint('from')">
        <strong>مبدأ: {{ pointPlace(from) }}</strong>
      </button>
      <button type="button" :aria-label="`تغییر مقصد، ${pointPlace(to)}`" @click="editEndpoint('to')">
        <strong>مقصد: {{ pointPlace(to) }}</strong>
      </button>
    </div>
    <p>ساعت‌ها به وقت تهران هستند. زمان تعویض خط: ۲ دقیقه.</p>
    <button class="button" type="submit" :disabled="loading">محاسبه مسیر</button>
  </form>
  <section v-if="origin && destination" class="card endpoints" aria-label="مبدأ و مقصد">
    <p><strong>مبدأ:</strong> {{ origin.nameFa }}</p>
    <p><strong>مقصد:</strong> {{ destination.nameFa }}</p>
    <small>ایستگاه‌ها با مقایسه زمان کل سفر و شیوه جابه‌جایی انتخابی در هر سمت، انتخاب شده‌اند.</small>
  </section>
  <p v-if="loading" class="card" role="status">در حال یافتن بهترین مسیر…</p>
  <section v-if="error" class="card" role="alert">
    <p>{{ error }} {{ result ? 'اطلاعات مربوط به آخرین دریافت موفق است.' : '' }}</p><button class="text-button" @click="search()">تلاش دوباره</button>
  </section>
  <section v-if="result?.walkOnly" class="card" role="status">
    <JourneyMap v-if="graph && from && to" :graph="graph" :from="from" :to="to" />
    <h2>پیشنهاد: پیاده‌روی تا مقصد</h2>
    <p>پیاده‌روی مستقیم در زمان انتخابی مناسب‌تر است یا قطار مناسبی در بازه جست‌وجو پیدا نشد.</p>
    <p>{{ duration(result.directWalkSeconds) }} · {{ result.directWalkMeters.toLocaleString('fa-IR') }} متر</p>
    <p>حرکت {{ dateLabel(result.departureLocal) }}، {{ timeLabel(result.departureLocal) }} · رسیدن {{ dateLabel(result.arrivalLocal) }}، {{ timeLabel(result.arrivalLocal) }}</p>
    <StreetLegDetails v-if="result.directPath" :path="result.directPath" />
  </section>
  <template v-else-if="result?.legs.length">
    <JourneyMap v-if="graph && from && to" :graph="graph" :from="from" :to="to" />
    <p v-else role="status">مختصات این مسیر برای نمایش روی نقشه کامل نیست.</p>
    <p class="map-note">خط‌چین: پیاده‌روی · نارنجی: مسیر خیابانی خودرو یا موتور · رنگ خطوط مترو: بخش ریلی (نمای شماتیک بین ایستگاه‌ها).</p>
    <section class="card itinerary" aria-label="مراحل سفر">
      <h2>مسیر پیشنهادی</h2>
      <p>حرکت از مبدأ: {{ dateLabel(result.departureLocal) }}، {{ timeLabel(result.departureLocal) }}</p>
      <p>رسیدن به مقصد: {{ dateLabel(result.arrivalLocal) }}، {{ timeLabel(result.arrivalLocal) }}</p>
      <p>کل سفر: {{ duration((localTime(result.arrivalLocal) - localTime(result.departureLocal)) / 1000) }}</p>
      <p v-if="result.timeMode === 'arriveBy'">آخرین زمان حرکت برای رسیدن تا {{ dateLabel(result.requestedTimeLocal) }}، {{ timeLabel(result.requestedTimeLocal) }}</p>
      <ol>
        <li v-if="result.access"><h3>{{ modeLabel(result.accessMode) }} تا {{ origin?.nameFa }}</h3>
          <p>{{ duration(result.access.seconds) }} · {{ result.access.distanceMeters.toLocaleString('fa-IR') }} متر (شامل ۲ دقیقه دسترسی به سکو)</p>
          <StreetLegDetails v-if="result.access.path" :path="result.access.path" />
        </li>
        <li v-for="(leg, index) in result.legs" :key="index">
          <h3>{{ stationName(leg.fromStationId) }}</h3>
          <p v-if="index">تعویض قطار<span v-if="leg.transfer"> · {{ duration(leg.transfer.minimumSeconds) }}</span></p>
          <p>{{ lineName(leg.lineId) }} · به سمت {{ stationName(leg.directionStationId) }}</p>
          <p>{{ timeLabel(leg.departureLocal) }} تا {{ timeLabel(leg.arrivalLocal) }} · {{ leg.stopCount.toLocaleString('fa-IR') }} ایستگاه</p>
          <p>مدت حرکت قطار: {{ duration((localTime(leg.arrivalLocal) - localTime(leg.departureLocal)) / 1000) }}</p>
          <p>انتظار پس از رسیدن شما: {{ waitMinutes(index) }}</p>
          <p v-if="leg.boardingArrivalLocal">ورود قطار به {{ stationName(leg.fromStationId) }}: {{ timeLabel(leg.boardingArrivalLocal) }}
            <span v-if="localTime(leg.boardingArrivalLocal) > now"> · {{ countdown(leg.boardingArrivalLocal) }} دقیقه دیگر</span>
            <span v-else-if="localTime(leg.departureLocal) >= now"> · توقف در ایستگاه</span>
          </p>
          <p>موقعیت تخمینی قطار: {{ trainLocation(leg) }}</p>
        </li>
        <li><h3>{{ destination?.nameFa }}</h3><p>{{ modeLabel(result.exitMode) }} تا مقصد: {{ duration(result.exit?.seconds ?? 0) }} · {{ result.exit?.distanceMeters.toLocaleString('fa-IR') }} متر (شامل ۲ دقیقه خروج از ایستگاه)</p>
          <StreetLegDetails v-if="result.exit?.path" :path="result.exit.path" />
        </li>
      </ol>
    </section>
  </template>
  <p v-if="hasMissingStreetPath" class="card route-warning" role="status">مسیر خیابانی برخی بخش‌ها دریافت نشد؛ زمان آن‌ها تخمینی است و مسیر ناموجود روی نقشه رسم نشده است.</p>
  <p v-if="result" class="map-note" role="status">زمان قطارها از برنامه پایگاه داده و زمان بخش خیابانی برآورد نشان است (خودرو بدون ترافیک)؛ زمان واقعی سفر ممکن است متفاوت باشد.
    هندسه دقیق ریل و راهروهای داخل ایستگاه در داده‌ها موجود نیست. آخرین دریافت {{ timeLabel(result.requestedAtLocal) }} · به‌روزرسانی هر ۳۰ ثانیه.</p>
</template>

<style scoped>
.endpoints { margin-block: 10px 20px; }
.trip-options { margin-block: 12px 20px; }
.trip-options > .button { width: 100%; }
.options-row, .endpoint-buttons { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; margin-block: 12px; }
.options-row > label { display: grid; min-width: 0; gap: 6px; font-size: 13px; }
.options-row input, .options-row select { width: 100%; min-width: 0; min-height: 46px; border: 1px solid var(--border); border-radius: 12px; padding: 8px; color: var(--ink); background: var(--canvas); }
.options-row input { direction: ltr; }
.time-choice { display: flex; flex-wrap: nowrap; gap: 24px; border: 0; padding: 0; margin: 12px 0; }
.time-choice label { display: inline-flex; align-items: center; gap: 8px; min-height: 44px; white-space: nowrap; cursor: pointer; }
.time-choice input { width: 18px; height: 18px; margin: 0; accent-color: var(--teal); }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
.endpoint-buttons button { display: grid; gap: 4px; min-width: 0; padding: 12px; border: 1px solid var(--border); border-radius: 12px; text-align: start; background: var(--active); color: var(--ink); }
.endpoint-buttons strong { font-size: 13px; overflow-wrap: anywhere; }
.trip-options > p, .route-warning { font-size: 13px; }
@media (max-width: 400px) { .trip-options { padding: 16px; } .options-row, .endpoint-buttons { gap: 8px; } .time-choice { gap: 20px; } }
.endpoints strong { color: var(--ink); }
.endpoints small, .map-note { color: var(--muted); font-size: 12px; }
.map-note { margin-block: 8px 20px; }
.itinerary ol { list-style: none; padding: 0; margin-bottom: 0; }
.itinerary li { position: relative; padding: 0 24px 22px 0; border-inline-start: 3px solid var(--teal); margin-inline-start: 8px; }
.itinerary li::before { content: ''; position: absolute; inset-inline-start: -8px; top: 8px; width: 13px; height: 13px; border: 3px solid var(--teal); border-radius: 50%; background: var(--canvas); }
.itinerary li:last-child { border-color: transparent; padding-bottom: 0; }
h3 { margin: 0; font-size: 18px; }
</style>
