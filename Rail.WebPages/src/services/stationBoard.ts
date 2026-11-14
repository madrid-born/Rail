import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue';
import type { Ref } from 'vue';
import { apiBase } from './api';
import type { TimetableTrip } from './timetable';
import type { MetroRoute, Station } from './network';

export interface BoardTrip extends Omit<TimetableTrip, 'position'> {
  serviceDate: string;
  position: TimetableTrip['position'] | null;
  stops: { sequence: number; stationId: string; arrivalAt: string; departureAt: string | null }[];
}
export interface NextArrival {
  routeId: string; directionStationId: string; tripId: number; serviceDate: string;
  arrivalAt: string; departureAt: string | null;
}
export interface StationBoard {
  stationId: string; lineId: number; requestedAt: string; until: string;
  nextArrivals: NextArrival[]; trips: BoardTrip[];
}
export function stationDirections(route: MetroRoute, stations: Map<string, Station>, board?: StationBoard) {
  const stops = [...route.stops].sort((a, b) => a.sequence - b.sequence);
  const indices = new Map(stops.map((stop, index) => [stop.stationId, index]));
  const operating = stops.filter(stop => {
    const station = stations.get(stop.stationId);
    return stop.status === 'operational' && (!station || (station.infrastructureStatus === 'operational'
      && station.lines.some(line => line.lineId === route.lineId && line.isOperational)));
  });
  return (['forward', 'backward'] as const).map(direction => {
    const candidates = (board?.nextArrivals ?? []).filter(arrival => arrival.routeId === route.id).flatMap(arrival => {
      const trip = board?.trips.find(trip => trip.id === arrival.tripId && trip.serviceDate === arrival.serviceDate && trip.routeId === route.id);
      if (!trip) return [];
      const journey = [...trip.stops].sort((a, b) => a.sequence - b.sequence);
      const first = indices.get(journey[0]?.stationId ?? '');
      const last = indices.get(journey[journey.length - 1]?.stationId ?? '');
      if (first === undefined || last === undefined || first === last) return [];
      if ((last > first ? 'forward' : 'backward') !== direction) return [];
      return [{ arrival, trip, destination: journey[journey.length - 1]!.stationId }];
    }).sort((a, b) => Date.parse(a.arrival.arrivalAt) - Date.parse(b.arrival.arrivalAt));
    const next = candidates[0];
    return { direction, label: direction === 'forward' ? '↓' : '↑',
      id: next?.destination ?? (direction === 'forward' ? operating[operating.length - 1] ?? stops[stops.length - 1] : operating[0] ?? stops[0])?.stationId,
      next: next?.arrival, trip: next?.trip };
  });
}
export const tripKey = (trip: Pick<BoardTrip, 'id' | 'serviceDate'>) => `${trip.id}:${trip.serviceDate}`;
export const timeLabel = (at: string) => new Intl.DateTimeFormat('fa-IR', {
  timeZone: 'Asia/Tehran', hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
}).format(new Date(at));
export function arrivalLabel(arrivalAt: string, at: string, departureAt: string | null = null) {
  if (Date.parse(arrivalAt) <= Date.parse(at) && (departureAt === null || Date.parse(departureAt) >= Date.parse(at))) return 'در ایستگاه';
  const minutes = Math.max(0, Math.ceil((Date.parse(arrivalAt) - Date.parse(at)) / 60_000));
  return `${new Intl.NumberFormat('fa-IR').format(minutes)} دقیقه دیگر`;
}
export function trainLocation(trip: BoardTrip, name: (id: string) => string) {
  const position = trip.position;
  if (!position) return 'هنوز حرکت نکرده است';
  if (position.station) return `در ایستگاه ${name(position.station)}`;
  return `بین ${name(position.fromStation ?? '')} و ${name(position.toStation ?? '')}`;
}
export function segmentProgress(trip: BoardTrip, at: string): number {
  const from = trip.stops.find(stop => stop.stationId === trip.position?.fromStation);
  const to = trip.stops.find(stop => stop.stationId === trip.position?.toStation);
  const departure = Date.parse(from?.departureAt ?? '');
  const arrival = Date.parse(to?.arrivalAt ?? '');
  if (!Number.isFinite(departure) || !Number.isFinite(arrival) || arrival <= departure) return .5;
  return Math.max(0, Math.min(1, (Date.parse(at) - departure) / (arrival - departure)));
}
export function useStationBoard(stationId: Ref<string>, lineId: Ref<number | undefined>) {
  const board = shallowRef<StationBoard>();
  const loading = ref(false);
  const failed = ref(false);
  const refreshToken = ref(0);
  let timer: ReturnType<typeof setInterval> | undefined;
  const refresh = () => { if (document.visibilityState === 'visible') refreshToken.value++; };
  watch([stationId, lineId, refreshToken], async ([id, line], _, onCleanup) => {
    if (board.value?.stationId !== id || board.value?.lineId !== line) board.value = undefined;
    if (line === undefined) return;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 20_000);
    let active = true;
    onCleanup(() => { active = false; controller.abort(); clearTimeout(timeout); });
    loading.value = true;
    failed.value = false;
    try {
      const response = await fetch(`${apiBase}/api/timetable/stations/${encodeURIComponent(id)}/board?lineId=${line}`, {
        signal: controller.signal, cache: 'no-store', headers: { Accept: 'application/json' },
      });
      if (!response.ok) throw new Error(`Station board HTTP ${response.status}`);
      const result: StationBoard = await response.json();
      if (active) board.value = result;
    } catch { if (active) failed.value = true; }
    finally { clearTimeout(timeout); if (active) loading.value = false; }
  }, { immediate: true });
  onMounted(() => {
    timer = setInterval(refresh, 30_000);
    document.addEventListener('visibilitychange', refresh);
  });
  onBeforeUnmount(() => { clearInterval(timer); document.removeEventListener('visibilitychange', refresh); });
  return { board, loading, failed, refresh };
}
