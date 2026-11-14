<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';
import type { MetroLine, MetroRoute, RouteStop, Station } from '../services/network';
import type { TimetableTrip } from '../services/timetable';
import RouteTrainTracks from './RouteTrainTracks.vue';
import { stationOccupancy } from '../services/trainPositions';

const props = defineProps<{
  line: MetroLine;
  lines: MetroLine[];
  stations: Map<string, Station>;
  routes: MetroRoute[];
  trips: readonly TimetableTrip[];
  open: boolean;
}>();
const emit = defineEmits<{ toggle: [open: boolean] }>();

const routes = computed(() => props.routes.map(route => ({
  ...route, stops: [...route.stops].sort((a, b) => a.sequence - b.sequence),
})).sort((a, b) => Number(Boolean(a.branchCode)) - Number(Boolean(b.branchCode))));
const number = new Intl.NumberFormat('fa-IR');
const color = (value: string) => /^#[\da-f]{6}$/i.test(value) ? value : 'var(--teal)';
const lineColor = computed(() => color(props.line.color));
const stationCount = computed(() => [...props.stations.values()]
  .filter(station => station.lines.some(line => line.lineId === props.line.id)).length);
const linesById = computed(() => new Map(props.lines.map(line => [line.id, line])));
const occupancy = computed(() => new Map(routes.value.map(route => [route.id, stationOccupancy(route, props.trips)])));

function connections(stop: RouteStop) {
  return (props.stations.get(stop.stationId)?.lines ?? [])
    .filter(line => line.lineId !== props.line.id)
    .map(line => ({
      id: line.lineId,
      name: linesById.value.get(line.lineId)?.nameFa ?? `خط ${number.format(line.lineId)}`,
      color: color(linesById.value.get(line.lineId)?.color ?? ''),
      operational: line.isOperational && props.stations.get(stop.stationId)?.infrastructureStatus === 'operational',
    }));
}

function status(stop: RouteStop) {
  const station = props.stations.get(stop.stationId);
  if (stop.status === 'under_construction' || station?.infrastructureStatus === 'under_construction') return 'در حال ساخت';
  if (stop.status !== 'operational' || (station && (station.infrastructureStatus !== 'operational'
      || !station.lines.some(line => line.lineId === props.line.id && line.isOperational)))) return 'غیرفعال';
  return '';
}

</script>

<template>
  <details class="line-card" :open="open" :style="{ '--line-color': lineColor }"
    @toggle="emit('toggle', ($event.target as HTMLDetailsElement).open)">
    <summary>
      <span class="line-swatch" aria-hidden="true"></span>
      <span class="line-title">
        <strong>{{ line.nameFa }}</strong>
        <small>{{ number.format(stationCount) }} ایستگاه<span v-if="line.routes.length > 1"> · {{ number.format(line.routes.length) }} مسیر</span></small>
      </span>
      <svg class="chevron" viewBox="0 0 24 24" aria-hidden="true"><path d="m6 9 6 6 6-6" /></svg>
    </summary>
    <div class="line-content">
      <p v-if="routes.length === 0" class="line-message">هنوز مسیری برای این خط ثبت نشده است.</p>
      <section v-for="route in routes" :key="route.id" class="route-section" :aria-label="route.branchCode ? 'مسیر انشعابی' : 'مسیر اصلی'">
        <header class="route-heading">
          <h2>{{ route.branchCode ? 'مسیر انشعابی' : 'مسیر اصلی' }}</h2>
          <p v-if="route.stops.length">از {{ route.stops[0]?.nameFa }} تا {{ route.stops[route.stops.length - 1]?.nameFa }}</p>
        </header>
        <p v-if="!route.stops.length" class="line-message">هنوز ایستگاهی برای این مسیر ثبت نشده است.</p>
        <RouteTrainTracks v-else :route="route" :trips="trips" :stations="stations">
        <ol class="station-list">
          <li v-for="stop in route.stops" :key="`${stop.sequence}-${stop.stationId}`" class="station-row" :data-station-id="stop.stationId" :data-sequence="stop.sequence">
            <span class="station-rails" aria-hidden="true"><span class="station-marker" :class="{
              'occupied-forward': occupancy.get(route.id)?.get(stop.stationId)?.forward,
              'occupied-backward': occupancy.get(route.id)?.get(stop.stationId)?.backward,
            }"></span></span>
            <RouterLink class="station-info" :to="{ name: 'specific-station', params: { stationId: stop.stationId } }">
              <div class="station-name"><strong>{{ stations.get(stop.stationId)?.nameFa ?? stop.nameFa }}</strong><span v-if="status(stop)" class="station-status">{{ status(stop) }}</span></div>
              <div v-if="connections(stop).length" class="connections" aria-label="خطوط دیگر این ایستگاه">
                <span class="connection-label">تقاطع با</span>
                <span v-for="connection in connections(stop)" :key="connection.id" class="connection" :class="{ inactive: !connection.operational }">
                  <i :style="{ backgroundColor: connection.color }" aria-hidden="true"></i>
                  {{ connection.name }}<span v-if="!connection.operational"> (غیرفعال)</span>
                </span>
              </div>
            </RouterLink>
            <small class="stop-number" aria-hidden="true">{{ number.format(stop.sequence) }}</small>
          </li>
        </ol>
        </RouteTrainTracks>
      </section>
    </div>
  </details>
</template>

<style scoped>
.line-card { border: 1px solid var(--border); border-radius: 22px; background: var(--surface); overflow: hidden; }
summary { display: flex; align-items: center; gap: 14px; padding: 20px 22px; cursor: pointer; list-style: none; min-height: 84px; }
summary::-webkit-details-marker { display: none; }
summary:focus-visible { outline-offset: -5px; border-radius: 22px; }
.line-swatch { width: 14px; height: 40px; border-radius: 8px; background: var(--line-color); box-shadow: inset 0 0 0 1px #102c3420; flex: none; }
.line-title { flex: 1; }
.line-title strong { display: block; font-size: 18px; }
.line-title small { display: block; color: var(--muted); font-size: 12px; }
.chevron { color: var(--muted); flex: none; }
details[open] .chevron { transform: rotate(180deg); }
.line-content { border-top: 1px solid var(--border); }
.line-message { padding: 20px 24px; }
.route-heading { padding: 20px 24px 8px; }
.route-heading h2 { font-size: 14px; margin: 0; }
.route-heading p { font-size: 12px; margin-top: 4px; }
.route-section + .route-section { border-top: 1px solid var(--border); }
.station-list { list-style: none; padding: 8px 24px 24px; margin: 0; }
.station-row { display: flex; gap: 16px; min-height: 64px; position: relative; align-items: flex-start; padding-block: 12px; }
.station-row:not(:last-child)::before { content: ''; position: absolute; width: 3px; inset-inline-start: 26.5px; top: 25.5px; bottom: -25.5px; background: var(--line-color); }
.station-rails { width: 56px; flex: none; display: flex; justify-content: center; }
.station-marker { --station-left: var(--canvas); --station-right: var(--canvas); position: relative; width: 19px; height: 19px; margin-top: 4px; border: 3px solid var(--line-color); border-radius: 50%; background: linear-gradient(90deg, var(--station-left) 50%, var(--station-right) 50%); flex: none; z-index: 1; }
.station-marker.occupied-forward { --station-left: #fff; }
.station-marker.occupied-backward { --station-right: #fff; }
.station-info { flex: 1; min-width: 0; text-decoration: none; border-radius: 6px; }
.station-info:hover .station-name strong { color: var(--teal); text-decoration: underline; }
.station-name { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; overflow-wrap: anywhere; }
.station-name strong { font-weight: 600; }
.station-status { font-size: 11px; color: var(--muted); padding: 1px 8px; border: 1px dashed var(--border); border-radius: 8px; }
.connections { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; margin-top: 6px; }
.connection-label { color: var(--muted); font-size: 11px; }
.connection { display: inline-flex; align-items: center; gap: 5px; padding: 2px 8px; border: 1px solid var(--border); border-radius: 20px; font-size: 11px; background: var(--canvas); }
.connection i { width: 8px; height: 8px; border-radius: 50%; box-shadow: inset 0 0 0 1px #102c3420; flex: none; }
.connection.inactive { border-style: dashed; color: var(--muted); }
.stop-number { color: var(--muted); font-size: 11px; padding-top: 4px; }
@media (max-width: 600px) {
  summary { padding: 16px; }
  .route-heading { padding-inline: 18px; }
  .station-list { padding-inline: 18px; }
  .station-row { gap: 12px; }
}
</style>
