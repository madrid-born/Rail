<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import type { MetroRoute, Station } from '../services/network';
import type { TimetableTrip } from '../services/timetable';
import { locateTrains } from '../services/trainPositions';

const props = defineProps<{ route: MetroRoute; trips: readonly TimetableTrip[]; stations: Map<string, Station> }>();
const container = ref<HTMLElement>();
const geometry = ref<{ width: number; height: number; center: number; stops: number[] }>();
let observer: ResizeObserver | undefined;
let frame: number | undefined;

function measure() {
  const element = container.value;
  if (!element) return;
  const bounds = element.getBoundingClientRect();
  const points = [...element.querySelectorAll<HTMLElement>('.station-marker')].map(marker => marker.getBoundingClientRect());
  if (!bounds.width || !bounds.height || !points.length) { geometry.value = undefined; return; }
  geometry.value = { width: bounds.width, height: bounds.height,
    center: points[0]!.left + points[0]!.width / 2 - bounds.left,
    stops: points.map(point => point.top + point.height / 2 - bounds.top) };
}

function scheduleMeasure() {
  if (frame !== undefined) cancelAnimationFrame(frame);
  frame = requestAnimationFrame(() => { frame = undefined; measure(); });
}

function observeRows() {
  observer?.disconnect();
  if (!container.value) return;
  observer?.observe(container.value);
  container.value.querySelectorAll('.station-row').forEach(row => observer?.observe(row));
  scheduleMeasure();
}

onMounted(() => { observer = new ResizeObserver(scheduleMeasure); observeRows(); });
watch(() => props.route, async () => { await nextTick(); observeRows(); }, { flush: 'post' });
watch(() => props.stations, scheduleMeasure, { flush: 'post' });
onBeforeUnmount(() => { observer?.disconnect(); if (frame !== undefined) cancelAnimationFrame(frame); });

const orderedStops = computed(() => [...props.route.stops].sort((a, b) => a.sequence - b.sequence));
const markers = computed(() => locateTrains(props.route, props.trips).flatMap(marker => {
  const layout = geometry.value;
  const fromY = layout?.stops[marker.fromIndex];
  const toY = layout?.stops[marker.toIndex];
  if (!layout || fromY === undefined || toY === undefined) return [];
  const stationName = (id: string) => props.stations.get(id)?.nameFa ?? id;
  const from = orderedStops.value[marker.fromIndex]!.stationId;
  const to = orderedStops.value[marker.toIndex]!.stationId;
  const place = from === to ? `ایستگاه ${stationName(from)}` : `بین ${stationName(from)} و ${stationName(to)}`;
  return [{ ...marker, x: layout.center + (marker.direction === 'forward' ? -18 : 18),
    y: (fromY + toY) / 2, label: `قطار به سمت ${stationName(marker.directionStationId)}، ${place}` }];
}));

const movingMarkers = computed(() => markers.value.filter(marker => marker.fromIndex !== marker.toIndex));
</script>

<template>
  <div ref="container" class="route-diagram">
    <svg v-if="geometry" class="train-tracks" :viewBox="`0 0 ${geometry.width} ${geometry.height}`"
      role="img" :aria-label="`${markers.length} قطار؛ موقعیت تقریبی بر اساس برنامه حرکت`">
      <g v-for="(direction, index) in ['forward', 'backward']" :key="direction" :data-track-direction="direction">
        <line class="direction-track" :x1="geometry.center + (index === 0 ? -18 : 18)" :x2="geometry.center + (index === 0 ? -18 : 18)"
          :y1="geometry.stops[0]" :y2="geometry.stops[geometry.stops.length - 1]" />
      </g>
      <path v-for="marker in movingMarkers" :key="marker.key" class="train-arrow"
        :d="marker.direction === 'forward'
          ? `M ${marker.x} ${marker.y + 6} L ${marker.x - 5} ${marker.y - 4} H ${marker.x - 1.5} V ${marker.y - 10} H ${marker.x + 1.5} V ${marker.y - 4} H ${marker.x + 5} Z`
          : `M ${marker.x} ${marker.y - 6} L ${marker.x - 5} ${marker.y + 4} H ${marker.x - 1.5} V ${marker.y + 10} H ${marker.x + 1.5} V ${marker.y + 4} H ${marker.x + 5} Z`"
        :data-trip-id="marker.tripId" :data-direction="marker.direction" :aria-label="marker.label">
        <title>{{ marker.label }}</title>
      </path>
    </svg>
    <slot />
  </div>
</template>

<style scoped>
.route-diagram { position: relative; }
.train-tracks { position: absolute; inset: 0; width: 100%; height: 100%; pointer-events: none; z-index: 2; overflow: visible; }
.direction-track { stroke: #000; stroke-width: 3; }
.train-arrow { fill: #fff; stroke: #000; stroke-width: 1.5; stroke-linejoin: round; pointer-events: auto; }
</style>
