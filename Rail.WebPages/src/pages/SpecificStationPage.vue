<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import PageHeading from '../components/PageHeading.vue';
import EmptyState from '../components/EmptyState.vue';
import StationNeighborhood from '../components/StationNeighborhood.vue';
import TrainJourney from '../components/TrainJourney.vue';
import { timeLabel, tripKey } from '../services/stationBoard';
import type { BoardTrip } from '../services/stationBoard';
import { useStationPage } from '../services/stationPage';

const route = useRoute();
const stationId = computed(() => String(route.params.stationId ?? ''));
const { page, board, loading, failed, selectedLineId, boardFailed, boardLoading, selectLine, refreshBoard, reload } = useStationPage(stationId);
const details = computed(() => page.value?.station);
const selectedTrainKey = ref<string>();
const selectedTrain = computed(() => board.value?.trips.find(trip => tripKey(trip) === selectedTrainKey.value));
function selectTrain(trip: BoardTrip) { selectedTrainKey.value = tripKey(trip); }
const station = details;
const info = details;
const stations = computed(() => new Map((page.value?.stations ?? []).map(item => [item.id, item])));
const availableLines = computed(() => page.value?.lines ?? []);
const selectedLine = computed(() => availableLines.value.find(line => line.id === selectedLineId.value));
const otherLines = computed(() => availableLines.value.filter(line => line.id !== selectedLineId.value));
const lineColor = computed(() => /^#[\da-f]{6}$/i.test(selectedLine.value?.color ?? '') ? selectedLine.value!.color : 'var(--teal)');
const color = (value: string) => /^#[\da-f]{6}$/i.test(value) ? value : 'var(--teal)';
function buttonTextColor(value: string) {
  if (!/^#[\da-f]{6}$/i.test(value)) return 'var(--canvas)';
  const [red, green, blue] = [1, 3, 5].map(start => parseInt(value.slice(start, start + 2), 16));
  return red! * 299 + green! * 587 + blue! * 114 >= 150000 ? '#102c34' : '#fff';
}
const routes = computed(() => (page.value?.routes ?? []).filter(item => item.lineId === selectedLineId.value
  && item.stops.some(stop => stop.stationId === stationId.value))
  .sort((a, b) => Number(Boolean(a.branchCode)) - Number(Boolean(b.branchCode))));
const amenityNames: Record<number, string> = { 1: 'سرویس بهداشتی', 2: 'آسانسور', 3: 'خودپرداز', 4: 'کافی‌شاپ',
  5: 'غذای آماده', 6: 'فروشگاه', 7: 'اینترنت رایگان', 8: 'نمازخانه', 9: 'پارکینگ', 10: 'پلیس' };
const status = (value: string) => value === 'operational' ? 'فعال' : value === 'under_construction' ? 'در حال ساخت' : 'غیرفعال';

watch([stationId, selectedLineId], () => { selectedTrainKey.value = undefined; });
</script>

<template>
  <div v-if="loading && !page" class="card" role="status">در حال دریافت ایستگاه…</div>
  <EmptyState v-else-if="failed && !page" icon="stations" title="اطلاعات دریافت نشد" description="اتصال اینترنت را بررسی کنید و دوباره تلاش کنید.">
    <button class="button" type="button" @click="reload">تلاش دوباره</button>
  </EmptyState>
  <EmptyState v-else-if="!station" icon="stations" title="ایستگاه پیدا نشد" description="از فهرست ایستگاه‌ها یک ایستگاه را انتخاب کنید." />
  <template v-else>
    <PageHeading :title="station.nameFa" :description="station.nameEn" />
    <section class="card nearby" :style="{ '--line-color': lineColor }" aria-labelledby="nearby-title">
      <h2 id="nearby-title" aria-live="polite">{{ selectedLine?.nameFa }} · ایستگاه‌های اطراف</h2>
      <div v-if="otherLines.length" class="line-picker">
        <button v-for="line in otherLines" :key="line.id" type="button" class="line-switch"
          :style="{ backgroundColor: color(line.color), color: buttonTextColor(line.color) }" @click="selectLine(line.id)">
          تغییر به خط {{ line.id }}
        </button>
      </div>
      <p class="help">زمان ورود و موقعیت قطارها بر اساس برنامه حرکت است. برای دیدن موقعیت و زمان رسیدن به تمام ایستگاه‌ها، قطار را انتخاب کنید.</p>
      <p v-if="board" class="help">آخرین دریافت: {{ timeLabel(board.requestedAt) }} · به‌روزرسانی هر ۳۰ ثانیه</p>
      <div v-if="boardFailed" class="notice" role="status">
        <p>{{ board ? 'به‌روزرسانی قطارها ممکن نشد؛ زمان‌ها و موقعیت‌ها مربوط به آخرین دریافت موفق است.' : 'زمان ورود و موقعیت قطارها در دسترس نیست.' }}</p>
        <button class="text-button" type="button" :disabled="boardLoading" @click="refreshBoard">تلاش دوباره</button>
      </div>
      <p v-else-if="!board && selectedLineId !== undefined" class="notice" role="status">در حال دریافت موقعیت و زمان ورود قطارها…</p>
      <p v-if="!routes.length">مسیری برای این ایستگاه در خط انتخاب‌شده ثبت نشده است.</p>
      <section v-for="item in routes" :key="item.id" class="route-section">
        <h3 v-if="routes.length > 1">{{ item.branchCode ? 'مسیر انشعابی' : 'مسیر اصلی' }}</h3>
        <StationNeighborhood :route="item" :station-id="stationId" :stations="stations" :board="board" @select-train="selectTrain" />
      </section>
    </section>
    <TrainJourney :trip="selectedTrain" :at="board?.requestedAt" :route="routes.find(item => item.id === selectedTrain?.routeId)" :station-id="stationId" :stations="stations" :stale="boardFailed" @close="selectedTrainKey = undefined" />
    <section class="card details" :class="info?.infrastructureStatus === 'operational' ? 'available' : 'unavailable'" aria-labelledby="details-title">
      <h2 id="details-title">دربارهٔ ایستگاه</h2>
      <dl>
        <div><dt>نام فارسی</dt><dd>{{ info?.nameFa }}</dd></div>
        <div><dt>نام انگلیسی</dt><dd lang="en" dir="ltr">{{ info?.nameEn }}</dd></div>
        <div><dt>وضعیت ایستگاه</dt><dd>{{ status(info?.infrastructureStatus ?? '') }}</dd></div>
        <div><dt>خطوط این ایستگاه</dt><dd class="station-lines"><span v-for="line in info?.lines" :key="line.lineId" :class="line.isOperational ? 'available' : 'unavailable'">{{ availableLines.find(item => item.id === line.lineId)?.nameFa ?? `خط ${line.lineId}` }} · {{ line.isOperational ? 'فعال' : 'غیرفعال' }}</span></dd></div>
        <div v-if="details"><dt>مختصات جغرافیایی</dt><dd dir="ltr">{{ details.latitude }}, {{ details.longitude }}</dd></div>
      </dl>
      <template v-if="details">
        <h3>امکانات</h3>
        <ul v-if="details.amenities.length" class="amenities">
          <li v-for="amenity in details.amenities" :key="amenity.type" :class="amenity.isAvailable ? 'available' : 'unavailable'">
            <strong>{{ amenityNames[amenity.type] ?? 'سایر امکانات' }}</strong>
            <span>{{ amenity.isAvailable ? 'موجود' : 'ناموجود' }} · {{ amenity.isVerified ? 'تأییدشده' : 'تأییدنشده' }}</span>
          </li>
        </ul>
        <p v-else>اطلاعات امکانات این ایستگاه هنوز ثبت نشده است.</p>
        <h3>ورودی‌ها</h3>
        <ul v-if="details.entrances.length" class="entrances">
          <li v-for="entrance in details.entrances" :key="entrance.code" :class="{ available: entrance.isStepFree === true, unavailable: entrance.isStepFree === false }">
            <strong>{{ entrance.nameFa }} <span dir="ltr">({{ entrance.code }})</span></strong>
            <p v-if="entrance.nameEn" lang="en">{{ entrance.nameEn }}</p>
            <p v-if="entrance.addressFa">{{ entrance.addressFa }}</p>
            <p v-if="entrance.addressEn" lang="en">{{ entrance.addressEn }}</p>
            <p>دسترسی بدون پله: {{ entrance.isStepFree === null ? 'نامشخص' : entrance.isStepFree ? 'دارد' : 'ندارد' }}</p>
            <p v-if="entrance.latitude !== null && entrance.longitude !== null" dir="ltr">{{ entrance.latitude }}, {{ entrance.longitude }}</p>
          </li>
        </ul>
        <p v-else>اطلاعات ورودی‌های این ایستگاه هنوز ثبت نشده است.</p>
      </template>
    </section>
  </template>
</template>

<style scoped>
.line-picker { display: grid; gap: 14px; margin-block: 12px 20px; }
.line-switch { display: flex; width: 100%; align-items: center; justify-content: center; text-align: center; min-height: 46px; padding: 10px 18px; border: 0; border-radius: 12px; font-weight: 600; }
.line-switch:hover { filter: brightness(.95); }
.nearby { border-top: 4px solid var(--line-color); }
.details { margin-top: 22px; }
.help, .notice { font-size: 12px; }
.notice { margin-top: 12px; }
.route-section + .route-section { border-top: 1px solid var(--border); margin-top: 16px; }
h3 { font-size: 16px; margin: 24px 0 12px; }
dl { margin: 0; }
dl > div { display: flex; justify-content: space-between; flex-wrap: wrap; gap: 12px; padding-block: 12px; border-bottom: 1px solid var(--border); }
dt { color: var(--muted); }
dd { margin: 0; overflow-wrap: anywhere; }
.station-lines { display: flex; flex-wrap: wrap; gap: 12px; }
.station-lines span { border: 2px solid var(--border); border-radius: 10px; padding: 4px 10px; }
.amenities, .entrances { list-style: none; padding: 0; margin: 0; display: grid; gap: 12px; }
.amenities { grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); }
.amenities li, .entrances li { border: 1px solid var(--border); border-radius: 14px; padding: 14px; }
.amenities span { display: block; font-size: 12px; color: var(--muted); }
.available { border-color: #269554 !important; border-width: 2px !important; }
.unavailable { border-color: #d14b4b !important; border-width: 2px !important; }
</style>
