import type { MetroRoute } from './network';
import type { TimetableTrip } from './timetable';

export interface TrainMarker {
  key: string;
  tripId: number;
  direction: 'forward' | 'backward';
  fromIndex: number;
  toIndex: number;
  directionStationId: string;
}

export function locateTrains(route: MetroRoute, trips: readonly TimetableTrip[]): TrainMarker[] {
  const stops = [...route.stops].sort((a, b) => a.sequence - b.sequence);
  const indices = new Map(stops.map((stop, index) => [stop.stationId, index]));
  const markers: TrainMarker[] = [];
  trips.forEach((trip, occurrence) => {
    if (trip.routeId !== route.id || trip.lineId !== route.lineId) return;
    const { station, fromStation, toStation } = trip.position;
    let fromIndex: number | undefined;
    let toIndex: number | undefined;
    let forward: boolean;
    if (station !== null) {
      fromIndex = toIndex = indices.get(station);
      const target = indices.get(trip.directionStationId);
      if (fromIndex === undefined || target === undefined) return;
      if (target === fromIndex) {
        if (target !== 0 && target !== stops.length - 1) return;
        forward = target === stops.length - 1;
      } else forward = target > fromIndex;
    } else {
      if (fromStation === null || toStation === null) return;
      fromIndex = indices.get(fromStation);
      toIndex = indices.get(toStation);
      if (fromIndex === undefined || toIndex === undefined || fromIndex === toIndex) return;
      forward = toIndex > fromIndex;
    }
    if (fromIndex === undefined || toIndex === undefined) return;
    markers.push({ key: `${trip.id}-${occurrence}`, tripId: trip.id, direction: forward ? 'forward' : 'backward',
      fromIndex, toIndex, directionStationId: trip.directionStationId });
  });
  return markers;
}

export function stationOccupancy(route: MetroRoute, trips: readonly TimetableTrip[]) {
  const stops = [...route.stops].sort((a, b) => a.sequence - b.sequence);
  const result = new Map<string, { forward: boolean; backward: boolean }>();
  for (const marker of locateTrains(route, trips)) {
    if (marker.fromIndex !== marker.toIndex) continue;
    const id = stops[marker.fromIndex]!.stationId;
    const occupied = result.get(id) ?? { forward: false, backward: false };
    occupied[marker.direction] = true;
    result.set(id, occupied);
  }
  return result;
}
