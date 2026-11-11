<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from 'vue';
import PageHeading from '../components/PageHeading.vue';
import EmptyState from '../components/EmptyState.vue';
import StationLine from '../components/StationLine.vue';
import { checkNetworkCache, useNetworkCache } from '../services/network';
import { startTimetablePolling, stopTimetablePolling, useTimetableCache } from '../services/timetable';

const { snapshot, loading, failed } = useNetworkCache();
const { snapshot: timetable, failed: timetableFailed } = useTimetableCache();
onMounted(startTimetablePolling);
onBeforeUnmount(stopTimetablePolling);
const lines = computed(() => [...(snapshot.value?.data.lines ?? [])].sort((a, b) => a.id - b.id));
const stationLookup = computed(() => new Map((snapshot.value?.data.stations ?? []).map(station => [station.id, station])));
</script>

<template>
  <PageHeading title="ایستگاه‌ها" eyebrow="هر ایستگاه، یک مقصد تازه" description="یک خط را باز کنید تا ایستگاه‌ها و تقاطع‌های آن را ببینید." />
  <p v-if="timetableFailed" class="timetable-unavailable" role="status">{{ timetable ? 'به‌روزرسانی موقعیت قطارها ممکن نشد؛ آخرین اطلاعات دریافت‌شده نمایش داده می‌شود.' : 'موقعیت قطارها در دسترس نیست.' }}</p>
  <p v-if="loading && !snapshot" class="card loading" role="status">در حال دریافت ایستگاه‌ها…</p>
  <div v-else-if="failed && !snapshot" role="alert">
    <EmptyState icon="stations" title="اطلاعات دریافت نشد" description="اتصال اینترنت و دسترسی به سرویس Rail را بررسی کنید و دوباره تلاش کنید.">
      <button class="button" type="button" @click="checkNetworkCache()">تلاش دوباره</button>
    </EmptyState>
  </div>
  <EmptyState v-else-if="!lines.length" icon="stations" title="هنوز خطی ثبت نشده" description="پس از ثبت خطوط، ایستگاه‌های هر خط اینجا نمایش داده می‌شوند." />
  <div v-else class="line-list" aria-label="خطوط متروی تهران">
    <StationLine v-for="line in lines" :key="line.id" :line="line" :lines="lines" :stations="stationLookup"
      :routes="snapshot?.data.routes.filter(route => route.lineId === line.id) ?? []"
      :trips="timetable?.trips.filter(trip => trip.lineId === line.id) ?? []" />
  </div>
</template>

<style scoped>
.line-list { display: grid; gap: 14px; }
.loading { color: var(--muted); }
.timetable-unavailable { color: var(--muted); font-size: 12px; margin-bottom: 16px; }
.button { font: inherit; border: 0; cursor: pointer; }
</style>
