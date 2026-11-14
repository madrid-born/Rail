<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { RouterLink } from 'vue-router';
import PageHeading from '../components/PageHeading.vue';
import EmptyState from '../components/EmptyState.vue';
import StationLine from '../components/StationLine.vue';
import { checkNetworkCache, useNetworkCache } from '../services/network';
import { startTimetablePolling, stopTimetablePolling, useTimetableCache } from '../services/timetable';
import { searchStations, stationSearchWords } from '../services/stationSearch';

const { snapshot, loading, failed } = useNetworkCache();
const { snapshot: timetable, failed: timetableFailed } = useTimetableCache();
onMounted(startTimetablePolling);
onBeforeUnmount(stopTimetablePolling);
const lines = computed(() => [...(snapshot.value?.data.lines ?? [])].sort((a, b) => a.id - b.id));
const stationLookup = computed(() => new Map((snapshot.value?.data.stations ?? []).map(station => [station.id, station])));
const query = ref('');
const words = computed(() => stationSearchWords(query.value));
const results = computed(() => searchStations(snapshot.value?.data.stations ?? [], words.value));
const linesById = computed(() => new Map(lines.value.map(line => [line.id, line])));
const number = new Intl.NumberFormat('fa-IR');
const lineColor = (color?: string) => /^#[\da-f]{6}$/i.test(color ?? '') ? color : 'var(--teal)';
</script>

<template>
  <PageHeading title="ایستگاه‌ها" eyebrow="هر ایستگاه، یک مقصد تازه" description="یک خط را باز کنید تا ایستگاه‌ها و تقاطع‌های آن را ببینید." />
  <form class="station-search" role="search" @submit.prevent>
    <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 4 4" /></svg>
    <input v-model="query" type="search" dir="rtl" aria-label="نام ایستگاه" placeholder="نام ایستگاه" autocomplete="off" spellcheck="false" />
    <button v-if="query" type="button" aria-label="پاک کردن جست‌وجو" @click="query = ''">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 6 12 12M6 18 18 6" /></svg>
    </button>
  </form>
  <p v-if="timetableFailed" class="timetable-unavailable" role="status">{{ timetable ? 'به‌روزرسانی موقعیت قطارها ممکن نشد؛ آخرین اطلاعات دریافت‌شده نمایش داده می‌شود.' : 'موقعیت قطارها در دسترس نیست.' }}</p>
  <p v-if="loading && !snapshot" class="card loading" role="status">در حال دریافت ایستگاه‌ها…</p>
  <div v-else-if="failed && !snapshot" role="alert">
    <EmptyState icon="stations" title="اطلاعات دریافت نشد" description="اتصال اینترنت و دسترسی به سرویس Rail را بررسی کنید و دوباره تلاش کنید.">
      <button class="button" type="button" @click="checkNetworkCache()">تلاش دوباره</button>
    </EmptyState>
  </div>
  <EmptyState v-else-if="!lines.length" icon="stations" title="هنوز خطی ثبت نشده" description="پس از ثبت خطوط، ایستگاه‌های هر خط اینجا نمایش داده می‌شوند." />
  <section v-else-if="words.length" aria-label="نتایج جست‌وجوی ایستگاه‌ها">
    <p class="search-count" role="status">{{ number.format(results.length) }} ایستگاه پیدا شد</p>
    <EmptyState v-if="!results.length" icon="stations" title="ایستگاهی پیدا نشد" description="واژه‌های کمتری از نام ایستگاه را به فارسی یا انگلیسی بنویسید.">
      <button class="button" type="button" @click="query = ''">نمایش همهٔ خطوط</button>
    </EmptyState>
    <ul v-else class="search-results">
      <li v-for="station in results" :key="station.id">
        <RouterLink :to="{ name: 'specific-station', params: { stationId: station.id } }" class="station-result card">
          <strong>{{ station.nameFa }}</strong>
          <span class="english-name" lang="en" dir="ltr">{{ station.nameEn }}</span>
          <span class="result-lines">
            <span v-for="line in station.lines" :key="line.lineId" class="result-line">
              <i :style="{ backgroundColor: lineColor(linesById.get(line.lineId)?.color) }" aria-hidden="true"></i>
              {{ linesById.get(line.lineId)?.nameFa ?? `خط ${number.format(line.lineId)}` }}
            </span>
          </span>
        </RouterLink>
      </li>
    </ul>
  </section>
  <div v-else class="line-list" aria-label="خطوط متروی تهران">
    <StationLine v-for="line in lines" :key="line.id" :line="line" :lines="lines" :stations="stationLookup"
      :routes="snapshot?.data.routes.filter(route => route.lineId === line.id) ?? []"
      :trips="timetable?.trips.filter(trip => trip.lineId === line.id) ?? []" />
  </div>
</template>

<style scoped>
.line-list { display: grid; gap: 14px; }
.station-search { display: flex; align-items: center; gap: 10px; width: 100%; margin-bottom: 20px; padding: 4px 14px; border: 1px solid var(--border); border-radius: 14px; background: var(--surface); }
.station-search:focus-within { border-color: var(--teal); }
.station-search > svg { flex: none; color: var(--muted); }
.station-search input { flex: 1; width: 100%; min-width: 0; min-height: 48px; padding: 8px 0; border: 0; background: transparent; color: var(--ink); font: inherit; outline: none; }
.station-search input::placeholder { color: var(--muted); }
.station-search input::-webkit-search-cancel-button { display: none; }
.station-search button { display: grid; place-items: center; flex: none; width: 44px; height: 44px; padding: 0; border: 0; border-radius: 10px; background: transparent; color: var(--muted); }
.search-count { margin-bottom: 12px; font-size: 13px; }
.search-results { list-style: none; display: grid; gap: 12px; padding: 0; margin: 0; }
.station-result { display: grid; gap: 6px; text-decoration: none; padding: 18px 22px; }
.station-result:hover { border-color: var(--teal); }
.english-name { color: var(--muted); text-align: start; font-size: 13px; }
.result-lines { display: flex; flex-wrap: wrap; gap: 12px; }
.result-line { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; }
.result-line i { width: 10px; height: 10px; border-radius: 50%; }
.loading { color: var(--muted); }
.timetable-unavailable { color: var(--muted); font-size: 12px; margin-bottom: 16px; }
.button { font: inherit; border: 0; cursor: pointer; }
</style>
