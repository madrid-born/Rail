<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import type { MetroRoute, Station } from '../services/network';
import type { BoardTrip } from '../services/stationBoard';
import { arrivalLabel, timeLabel, trainLocation } from '../services/stationBoard';

const props = defineProps<{ trip?: BoardTrip; at?: string; route?: MetroRoute; stationId: string; stations: Map<string, Station>; stale: boolean }>();
const emit = defineEmits<{ close: [] }>();
const dialog = ref<HTMLDialogElement>();
const body = ref<HTMLElement>();
const name = (id: string) => props.stations.get(id)?.nameFa ?? id;
const segmentDeparture = computed(() => props.trip?.stops.find(stop => stop.stationId === props.trip?.position?.fromStation)?.departureAt);
const segmentArrival = computed(() => props.trip?.stops.find(stop => stop.stationId === props.trip?.position?.toStation)?.arrivalAt);
const rows = computed(() => {
  if (!props.trip) return [];
  const stops = [...(props.route?.stops ?? [])].sort((a, b) => a.sequence - b.sequence);
  const first = stops.findIndex(stop => stop.stationId === props.trip!.stops[0]?.stationId);
  const last = stops.findIndex(stop => stop.stationId === props.trip!.stops[props.trip!.stops.length - 1]?.stationId);
  if (last < first) stops.reverse();
  return stops.map(stop => ({ ...stop, time: props.trip!.stops.find(time => time.stationId === stop.stationId) }));
});
watch(() => props.trip, async (trip, previous) => {
  await nextTick();
  const element = dialog.value;
  if (!element) return;
  if (!trip) { if (element.open) element.close(); return; }
  const opening = !element.open || trip.id !== previous?.id || trip.serviceDate !== previous?.serviceDate;
  if (!element.open) element.showModal();
  if (!opening) return;
  const scrollArea = body.value;
  if (!scrollArea) return;
  const targetId = props.stationId || trip.position?.station || trip.position?.toStation;
  const target = [...scrollArea.querySelectorAll<HTMLElement>('[data-station-id]')]
    .find(row => row.dataset.stationId === targetId);
  if (target) {
    const top = scrollArea.scrollTop + target.getBoundingClientRect().top
      - scrollArea.getBoundingClientRect().top - scrollArea.clientTop
      - (scrollArea.clientHeight - target.offsetHeight) / 2;
    scrollArea.scrollTo({ top: Math.max(0, top), behavior: 'auto' });
  }
}, { immediate: true });
const isPast = (at: string) => Date.parse(at) < Date.parse(props.at ?? '');
</script>

<template>
  <dialog ref="dialog" class="journey" aria-labelledby="journey-title" @close="emit('close')" @click="event => { if (event.target === dialog) dialog?.close(); }">
    <template v-if="trip && at">
      <header>
        <h2 id="journey-title">قطار به سمت {{ name(trip.directionStationId) }}</h2>
        <button type="button" class="text-button" autofocus @click="dialog?.close()">بستن</button>
      </header>
      <div ref="body" class="journey-body">
      <p class="position"><strong>{{ trainLocation(trip, name) }}</strong></p>
      <p>موقعیت و زمان‌ها بر اساس برنامه حرکت؛ به‌روزرسانی {{ timeLabel(at) }}</p>
      <p v-if="stale" class="stale" role="status">به‌روزرسانی ممکن نشد؛ این اطلاعات مربوط به آخرین دریافت موفق است.</p>
      <p v-if="trip.position?.fromStation && trip.position.toStation" class="segment-times">
        خروج از {{ name(trip.position.fromStation) }}: {{ segmentDeparture ? timeLabel(segmentDeparture) : 'نامشخص' }}؛
        ورود به {{ name(trip.position.toStation) }}: {{ segmentArrival ? timeLabel(segmentArrival) : 'نامشخص' }}
      </p>
      <ol class="journey-stops">
        <li v-for="stop in rows" :key="stop.stationId" :data-station-id="stop.stationId" :class="{ selected: stationId === stop.stationId, current: trip.position?.station === stop.stationId, passed: stop.time && isPast(stop.time.departureAt ?? stop.time.arrivalAt) }">
          <strong>{{ name(stop.stationId) }}</strong>
          <small v-if="stationId === stop.stationId">ایستگاه انتخاب‌شده</small>
          <template v-if="stop.time">
            <span>ورود {{ timeLabel(stop.time.arrivalAt) }}<template v-if="stop.time.departureAt"> · خروج {{ timeLabel(stop.time.departureAt) }}</template></span>
            <small v-if="isPast(stop.time.departureAt ?? stop.time.arrivalAt)">عبور کرده</small>
            <small v-else>{{ arrivalLabel(stop.time.arrivalAt, at, stop.time.departureAt) }}</small>
            <small v-if="stop.time.arrivalAt.slice(0, 10) !== at.slice(0, 10)">{{ new Intl.DateTimeFormat('fa-IR', { timeZone: 'Asia/Tehran', month: 'short', day: 'numeric' }).format(new Date(stop.time.arrivalAt)) }}</small>
          </template>
          <small v-else>این قطار در این ایستگاه توقف ندارد</small>
        </li>
      </ol>
      </div>
    </template>
  </dialog>
</template>

<style scoped>
.journey { width: min(640px, calc(100% - 24px)); max-height: 85dvh; padding: 0; overflow: hidden; border: 1px solid var(--border); border-radius: 22px; background: var(--canvas); color: var(--ink); }
.journey[open] { display: flex; flex-direction: column; }
.journey-body { min-height: 0; overflow-y: auto; overscroll-behavior: contain; padding: 0 24px 24px; }
.journey::backdrop { background: #001c2599; backdrop-filter: blur(4px); }
header { display: flex; flex: none; gap: 16px; align-items: start; justify-content: space-between; padding: 20px 24px 12px; border-bottom: 1px solid var(--border); }
h2 { font-size: 19px; }
header button { flex: none; padding-top: 0; }
.position { color: var(--teal); font-size: 17px; margin-block: 10px; }
p { font-size: 12px; }
.segment-times, .stale { margin-top: 10px; }
.stale { border-inline-start: 3px solid #ca503d; padding-inline-start: 10px; }
.journey-stops { list-style: none; padding: 0; margin: 20px 0 0; }
.journey-stops li { display: grid; gap: 4px; border-inline-start: 3px solid var(--teal); padding: 12px 18px; margin-inline-start: 6px; position: relative; }
.journey-stops li::before { content: ''; position: absolute; inset-inline-start: -7px; top: 20px; width: 11px; height: 11px; border-radius: 50%; background: var(--teal); }
.journey-stops span, .journey-stops small { font-size: 12px; color: var(--muted); }
.journey-stops .current { background: var(--active); border-radius: 10px; }
.journey-stops .selected { outline: 2px solid var(--teal); border-radius: 10px; opacity: 1; }
.journey-stops .passed { opacity: .65; }
</style>
