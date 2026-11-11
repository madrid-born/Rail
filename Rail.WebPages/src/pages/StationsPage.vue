<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import PageHeading from '../components/PageHeading.vue';
import EmptyState from '../components/EmptyState.vue';
import StationLine from '../components/StationLine.vue';
import { loadCachedData } from '../services/network';
import type { CachedNetwork } from '../services/network';

const snapshot = ref<CachedNetwork>();
const lines = computed(() => [...(snapshot.value?.data.lines ?? [])].sort((a, b) => a.id - b.id));
const stationLookup = computed(() => new Map((snapshot.value?.data.stations ?? []).map(station => [station.id, station])));
const savedAt = computed(() => snapshot.value
  ? new Intl.DateTimeFormat('fa-IR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(snapshot.value.savedAtUtc)) : '');
const loading = ref(true);
const failed = ref(false);
let active = true;

async function loadNetwork(refresh = false) {
  loading.value = true;
  failed.value = false;
  try {
    const data = await loadCachedData(refresh);
    if (active) snapshot.value = data;
  } catch {
    if (active) failed.value = true;
  } finally {
    if (active) loading.value = false;
  }
}

onMounted(() => loadNetwork());
onBeforeUnmount(() => { active = false; });
</script>

<template>
  <PageHeading title="ایستگاه‌ها" eyebrow="هر ایستگاه، یک مقصد تازه" description="یک خط را باز کنید تا ایستگاه‌ها و تقاطع‌های آن را ببینید." />
  <div v-if="snapshot" class="cache-toolbar">
    <p>{{ snapshot.persisted ? 'ذخیره‌شده روی دستگاه' : 'ذخیره موقت در برنامه' }} · {{ savedAt }}</p>
    <button class="refresh-button" type="button" :disabled="loading" @click="loadNetwork(true)">{{ loading ? 'در حال به‌روزرسانی…' : 'به‌روزرسانی اطلاعات' }}</button>
  </div>
  <p v-if="snapshot && !snapshot.persisted" class="cache-notice" role="status">ذخیرهٔ اطلاعات روی دستگاه ممکن نشد. این نسخه فقط تا زمان بستن برنامه در دسترس است.</p>
  <p v-if="snapshot && failed" class="cache-notice" role="alert">به‌روزرسانی انجام نشد؛ اطلاعات ذخیره‌شده همچنان در دسترس است.</p>
  <p v-if="loading && !snapshot" class="card loading" role="status">در حال دریافت و ذخیرهٔ خطوط و ایستگاه‌ها…</p>
  <div v-else-if="failed && !snapshot" role="alert">
    <EmptyState icon="stations" title="اطلاعات دریافت نشد" description="اتصال اینترنت و دسترسی به سرویس Rail را بررسی کنید و دوباره تلاش کنید.">
      <button class="button" type="button" @click="loadNetwork()">تلاش دوباره</button>
    </EmptyState>
  </div>
  <EmptyState v-else-if="!lines.length" icon="stations" title="هنوز خطی ثبت نشده" description="پس از ثبت خطوط، ایستگاه‌های هر خط اینجا نمایش داده می‌شوند." />
  <div v-else class="line-list" aria-label="خطوط متروی تهران">
    <StationLine v-for="line in lines" :key="line.id" :line="line" :lines="lines" :stations="stationLookup"
      :routes="snapshot?.data.routes.filter(route => route.lineId === line.id) ?? []" />
  </div>
</template>

<style scoped>
.line-list { display: grid; gap: 14px; }
.loading { color: var(--muted); }
.button { font: inherit; border: 0; cursor: pointer; }
.cache-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: 18px; }
.cache-toolbar p, .cache-notice { font-size: 12px; }
.cache-notice { padding: 12px; margin-bottom: 16px; border: 1px solid var(--border); border-radius: 12px; }
.refresh-button { font: inherit; font-size: 12px; color: var(--teal); background: var(--surface); border: 1px solid var(--border); border-radius: 12px; min-height: 44px; padding: 8px 12px; cursor: pointer; }
.refresh-button:disabled { opacity: .6; cursor: wait; }
</style>
