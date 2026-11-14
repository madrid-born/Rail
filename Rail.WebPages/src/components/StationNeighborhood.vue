<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';
import type { MetroRoute, Station } from '../services/network';
import type { BoardTrip, StationBoard } from '../services/stationBoard';
import { arrivalLabel, segmentProgress, stationDirections, timeLabel, trainLocation, tripKey } from '../services/stationBoard';
import { locateTrains } from '../services/trainPositions';
import { stationWindow } from '../services/stationDetails';

const props = defineProps<{ route: MetroRoute; stationId: string; stations: Map<string, Station>; board?: StationBoard }>();
const emit = defineEmits<{ selectTrain: [trip: BoardTrip] }>();
const window = computed(() => stationWindow(props.route, props.stationId));
const name = (id: string) => props.stations.get(id)?.nameFa ?? id;
const directions = computed(() => stationDirections(props.route, props.stations, props.board));
const activeTrips = computed(() => (props.board?.trips ?? []).filter(trip => trip.routeId === props.route.id && trip.position !== null));
const trainGroups = computed(() => {
  const indices = new Map([...props.route.stops].sort((a, b) => a.sequence - b.sequence).map((stop, index) => [stop.stationId, index]));
  const journeys = activeTrips.value.map(trip => {
    const stops = [...trip.stops].sort((a, b) => a.sequence - b.sequence);
    const first = indices.get(stops[0]?.stationId ?? '');
    const last = indices.get(stops[stops.length - 1]?.stationId ?? '');
    const direction = first !== undefined && last !== undefined && first !== last
      ? (last > first ? 'forward' : 'backward')
      : locateTrains(props.route, [{ ...trip, position: trip.position! }])[0]?.direction;
    return { trip, direction, destination: stops[stops.length - 1]?.stationId ?? trip.directionStationId };
  });
  return directions.value.map(direction => {
    const trains = journeys.filter(journey => journey.direction === direction.direction);
    const destinations = [...new Set(trains.map(train => name(train.destination)))];
    return { ...direction, trains, heading: destinations.length ? destinations.join(' / ') : direction.id ? name(direction.id) : '—' };
  });
});
const markers = computed(() => activeTrips.value.flatMap(trip => {
  const marker = locateTrains(props.route, [{ ...trip, position: trip.position! }])[0];
  if (!marker) return [];
  const progress = segmentProgress(trip, props.board!.requestedAt);
  const index = marker.fromIndex + (marker.toIndex - marker.fromIndex) * progress - window.value.start;
  if (index < 0 || index > window.value.stops.length - 1) return [];
  return [{ trip, direction: marker.direction, top: 56 + index * 112 }];
}));
</script>

<template>
  <div class="arrival-sides">
    <section v-for="direction in directions" :key="direction.direction" class="arrival-card" :data-direction="direction.direction">
      <h4>{{ direction.label }} به سمت {{ direction.id ? name(direction.id) : '—' }}</h4>
      <template v-if="board">
        <button v-if="direction.next && direction.trip" type="button" class="next-train" @click="emit('selectTrain', direction.trip)">
          <strong>{{ arrivalLabel(direction.next.arrivalAt, board.requestedAt, direction.next.departureAt) }}</strong>
          <span>ورود {{ timeLabel(direction.next.arrivalAt) }} · جزئیات قطار</span>
        </button>
        <p v-else>در ۲۴ ساعت آینده قطاری برای این جهت ثبت نشده است.</p>
      </template>
      <p v-else>زمان رسیدن در دسترس نیست.</p>
    </section>
  </div>
  <div class="neighborhood">
    <div class="rails" aria-hidden="true" :style="{ height: `${Math.max(0, window.stops.length - 1) * 112}px` }"></div>
    <ol class="stops">
      <li v-for="stop in window.stops" :key="stop.stationId" :class="{ selected: stop.stationId === stationId }">
        <span class="station-marker" aria-hidden="true"></span>
        <RouterLink :to="{ name: 'specific-station', params: { stationId: stop.stationId } }" :aria-current="stop.stationId === stationId ? 'location' : undefined">
          <strong>{{ name(stop.stationId) }}</strong>
          <small v-if="stop.stationId === stationId">ایستگاه انتخاب‌شده</small>
          <small v-if="stop.status !== 'operational'">{{ stop.status === 'under_construction' ? 'در حال ساخت' : 'غیرفعال' }}</small>
          <small v-if="(stations.get(stop.stationId)?.lines.length ?? 0) > 1">ایستگاه تقاطعی</small>
        </RouterLink>
      </li>
    </ol>
    <button v-for="marker in markers" :key="tripKey(marker.trip)" type="button" class="train-marker" :class="marker.direction"
      :style="{ top: `${marker.top}px` }" :aria-label="`جزئیات قطار؛ ${trainLocation(marker.trip, name)}`"
      :title="trainLocation(marker.trip, name)" @click="emit('selectTrain', marker.trip)">
      <svg viewBox="0 0 24 24" aria-hidden="true" :class="{ reverse: marker.direction === 'backward' }">
        <path d="M 12 22 L 5.5 12 H 9.5 V 2 H 14.5 V 12 H 18.5 Z" />
      </svg>
    </button>
  </div>
  <details v-if="activeTrips.length" class="all-trains">
    <summary>قطارهای در حال حرکت در این مسیر ({{ new Intl.NumberFormat('fa-IR').format(activeTrips.length) }})</summary>
    <div class="train-groups">
      <section v-for="group in trainGroups" :key="group.direction" class="train-group" :aria-label="`قطارها به سمت ${group.heading}`">
        <h4>{{ group.label }} به سمت {{ group.heading }} <small>({{ new Intl.NumberFormat('fa-IR').format(group.trains.length) }})</small></h4>
        <button v-for="{ trip, destination } in group.trains" :key="tripKey(trip)" type="button" class="train-row" @click="emit('selectTrain', trip)">
          <strong>{{ trainLocation(trip, name) }}</strong>
          <span>قطار {{ new Intl.NumberFormat('fa-IR', { useGrouping: false }).format(trip.id) }} · به سمت {{ name(destination) }}</span>
          <small>مشاهدهٔ زمان رسیدن به ایستگاه‌ها</small>
        </button>
        <p v-if="!group.trains.length" class="no-trains">اکنون قطاری در این جهت در حال حرکت نیست.</p>
      </section>
    </div>
  </details>
  <p v-else-if="board" class="no-trains">اکنون قطاری در حال حرکت در این مسیر نیست.</p>
</template>

<style scoped>
.arrival-sides { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; direction: ltr; margin-block: 20px; }
.arrival-card { direction: rtl; border: 1px solid var(--border); border-top: 3px solid var(--line-color); border-radius: 14px; padding: 14px; }
h4 { margin: 0 0 8px; font-size: 13px; }
.arrival-card p { font-size: 12px; }
.next-train { display: grid; gap: 6px; text-align: start; width: 100%; border: 0; background: transparent; color: var(--ink); padding: 0; }
.next-train strong { color: var(--teal); font-size: 18px; }
.next-train span { font-size: 12px; }
.neighborhood { --rail-center: 66px; --track-gap: 40px; position: relative; direction: ltr; }
.rails { position: absolute; top: 56px; left: calc(var(--rail-center) - var(--track-gap)); width: calc(2 * var(--track-gap)); border-inline: 3px solid #000; background: linear-gradient(90deg, transparent calc(50% - 2px), var(--line-color) calc(50% - 2px), var(--line-color) calc(50% + 2px), transparent calc(50% + 2px)); }
.stops { list-style: none; margin: 0; padding: 0; }
.stops li { height: 112px; display: flex; align-items: center; gap: 56px; padding-left: calc(var(--rail-center) - 12px); }
.station-marker { width: 24px; height: 24px; flex: none; border: 3px solid var(--line-color); border-radius: 50%; background: var(--canvas); z-index: 1; }
.selected .station-marker { outline: 3px solid var(--ink); outline-offset: 4px; }
.stops a { direction: rtl; text-align: start; text-decoration: none; flex: 1; padding: 8px; border-radius: 10px; }
.selected a { background: var(--active); }
.stops strong { font-size: 14px; }
.stops small { display: block; font-size: 11px; color: var(--muted); }
.train-marker { position: absolute; transform: translate(-50%, -50%); width: 44px; height: 44px; display: grid; place-items: center; padding: 0; background: transparent; border: 0; z-index: 2; }
.train-marker.forward { left: calc(var(--rail-center) - var(--track-gap) + 1.5px); }
.train-marker.backward { left: calc(var(--rail-center) + var(--track-gap) - 1.5px); }
.train-marker svg { width: 24px; height: 28px; fill: #fff; stroke: none; }
.train-marker svg.reverse { transform: rotate(180deg); }
.all-trains { margin-top: 12px; }
.all-trains summary { cursor: pointer; font-size: 13px; }
.train-groups { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 260px), 1fr)); gap: 16px; margin-top: 16px; }
.train-group { min-width: 0; border: 1px solid var(--border); border-top: 3px solid var(--line-color); border-radius: 14px; padding: 14px; }
.train-group h4 { margin-bottom: 12px; }
.train-group h4 small { color: var(--muted); }
.train-row { width: 100%; display: grid; gap: 4px; text-align: start; border: 1px solid var(--border); background: var(--canvas); color: var(--ink); padding: 12px; border-radius: 12px; margin-top: 8px; }
.train-row span, .train-row small, .no-trains { font-size: 12px; color: var(--muted); }
@media (max-width: 420px) { .arrival-card { padding: 10px; } .next-train strong { font-size: 16px; } }
</style>
