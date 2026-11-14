import type { FeatureCollection, LineString, Point } from 'geojson';
import type { NetworkCacheData, Station } from './network';

export type MapPoint = [number, number];
export interface JourneyLeg {
  tripId: number;
  serviceDate: string;
  routeId: string;
  lineId: number;
  directionStationId: string;
  fromStationId: string;
  toStationId: string;
  departureLocal: string;
  arrivalLocal: string;
  stopCount: number;
  transfer: { minimumSeconds: number; confidence: string; usedFallback: boolean } | null;
  boardingArrivalLocal: string | null;
  trainPosition: { status: string; stationId: string | null; fromStationId: string | null; toStationId: string | null } | null;
}
export interface JourneyResult { legs: JourneyLeg[] | null }
export type TravelMode = 'walk' | 'car' | 'motorcycle';
export interface StreetPath {
  mode: TravelMode; distanceMeters: number; seconds: number; coordinates: MapPoint[];
  steps: { instruction: string; distanceMeters: number; seconds: number }[];
  source: 'neshan' | 'estimated' | 'no_route';
}
export interface WalkingAccess { stationId: string; distanceMeters: number; seconds: number; path: StreetPath | null }
export interface WalkingJourneyResult {
  requestedAtLocal: string;
  arrivalLocal: string;
  departureLocal: string;
  requestedTimeLocal: string;
  timeMode: 'departAt' | 'arriveBy';
  accessMode: TravelMode;
  exitMode: TravelMode;
  transferMinutes: number;
  directPath: StreetPath | null;
  dataSource: 'timetable';
  walkOnly: boolean;
  access: WalkingAccess | null;
  exit: WalkingAccess | null;
  directWalkSeconds: number;
  directWalkMeters: number;
  legs: JourneyLeg[];
}

export function parseMapPoint(value: unknown): MapPoint | undefined {
  if (typeof value !== 'string' || !/^-?\d+(?:\.\d+)?,-?\d+(?:\.\d+)?$/.test(value)) return;
  const [lng, lat] = value.split(',').map(Number);
  if (lng !== undefined && lat !== undefined && Math.abs(lng) <= 180 && Math.abs(lat) <= 90) return [lng, lat];
}

export function nearestStation(network: NetworkCacheData, point: MapPoint): Station | undefined {
  const usable = new Set(network.routes.flatMap(route => route.stops.filter(stop => stop.status === 'operational')
    .filter(() => network.lines.some(line => line.id === route.lineId))
    .map(stop => `${route.lineId}:${stop.stationId}`)));
  let nearest: Station | undefined;
  let best = Infinity;
  const radians = (degrees: number) => degrees * Math.PI / 180;
  for (const station of network.stations) {
    if (station.infrastructureStatus !== 'operational'
      || !station.lines.some(line => line.isOperational && usable.has(`${line.lineId}:${station.id}`))
      || !Number.isFinite(station.latitude) || !Number.isFinite(station.longitude)
      || Math.abs(station.latitude) > 90 || Math.abs(station.longitude) > 180
      || (station.latitude === 0 && station.longitude === 0)) continue;
    const distance = Math.sin(radians(station.latitude - point[1]) / 2) ** 2
      + Math.cos(radians(point[1])) * Math.cos(radians(station.latitude))
      * Math.sin(radians(station.longitude - point[0]) / 2) ** 2;
    if (distance < best) { best = distance; nearest = station; }
  }
  return nearest;
}

export function journeyGraph(network: NetworkCacheData, legs: JourneyLeg[]): FeatureCollection<Point | LineString> {
  const features: FeatureCollection<Point | LineString>['features'] = [];
  const stations = new Map(network.stations.map(station => [station.id, station]));
  const included = new Set<string>();
  const named = new Set(legs.flatMap(leg => [leg.fromStationId, leg.toStationId]));
  for (const leg of legs) {
    const route = network.routes.find(route => route.id === leg.routeId);
    if (!route) throw new Error('Missing route');
    const stops = [...route.stops].sort((a, b) => a.sequence - b.sequence);
    const start = stops.findIndex(stop => stop.stationId === leg.fromStationId);
    const end = stops.findIndex(stop => stop.stationId === leg.toStationId);
    if (start < 0 || end < 0) throw new Error('Missing route endpoint');
    const segment = stops.slice(Math.min(start, end), Math.max(start, end) + 1);
    if (start > end) segment.reverse();
    const color = network.lines.find(line => line.id === leg.lineId)?.color ?? '#00636a';
    let previous: Station | undefined;
    for (const stop of segment) {
      const station = stations.get(stop.stationId);
      if (!station || !Number.isFinite(station.longitude) || !Number.isFinite(station.latitude)
        || Math.abs(station.latitude) > 90 || Math.abs(station.longitude) > 180
        || (station.longitude === 0 && station.latitude === 0)) { previous = undefined; continue; }
      if (previous) features.push({ type: 'Feature', geometry: { type: 'LineString', coordinates: [
        [previous.longitude, previous.latitude], [station.longitude, station.latitude],
      ] }, properties: { color: /^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(color) ? color : '#00636a' } });
      if (!included.has(station.id)) {
        included.add(station.id);
        const interchange = station.lines.filter(line => line.isOperational).length > 1;
        features.push({ type: 'Feature', geometry: { type: 'Point', coordinates: [station.longitude, station.latitude] },
          properties: { stationId: station.id, name: station.nameFa, named: named.has(station.id) || interchange } });
      }
      previous = station;
    }
  }
  return { type: 'FeatureCollection', features };
}
