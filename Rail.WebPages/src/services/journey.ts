import type { FeatureCollection, LineString, Point } from 'geojson';
import type { NetworkCacheData, Station } from './network';
import { segmentCoordinates, findSegment } from './metroGraph';

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
  source: 'graphhopper' | 'estimated' | 'no_route';
  trafficModel: 'none' | 'assumed_day_hour_v1';
  baselineSeconds?: number | null;
}
export interface WalkingAccess { stationId: string; distanceMeters: number; seconds: number; path: StreetPath | null }
export interface JourneyOption {
  id: string;
  totalSeconds: number;
  walkOnly: boolean;
  access: WalkingAccess | null;
  exit: WalkingAccess | null;
  legs: JourneyLeg[];
  departureLocal: string;
  arrivalLocal: string;
}
export interface WalkingJourneyResult extends JourneyOption {
  options?: JourneyOption[];
  requestedAtLocal: string;
  arrivalLocal: string;
  departureLocal: string;
  requestedTimeLocal: string;
  timeMode: 'departAt' | 'arriveBy';
  accessMode: TravelMode;
  exitMode: TravelMode;
  transferMinutes: number;
  platformSeconds?: number;
  viaStationIds: string[];
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

export function intermediateStations(network: NetworkCacheData | undefined, leg: JourneyLeg): Station[] {
  const route = network?.routes.find(route => route.id === leg.routeId);
  if (!route || !network) return [];
  const stops = [...route.stops].sort((a, b) => a.sequence - b.sequence);
  const start = stops.findIndex(stop => stop.stationId === leg.fromStationId);
  const end = stops.findIndex(stop => stop.stationId === leg.toStationId);
  if (start < 0 || end < 0) return [];
  const between = stops.slice(Math.min(start, end) + 1, Math.max(start, end));
  if (start > end) between.reverse();
  const stations = new Map(network.stations.map(station => [station.id, station]));
  return between.filter(stop => stop.status === 'operational').map(stop => stations.get(stop.stationId))
    .filter((station): station is Station => !!station);
}

export function journeyLabelStations(legs: JourneyLeg[]): string[] {
  if (!legs.length) return [];
  const stations = [legs[0]!.fromStationId];
  for (let index = 1; index < legs.length; index++) {
    const previous = legs[index - 1]!, next = legs[index]!;
    if (previous.toStationId === next.fromStationId && previous.lineId !== next.lineId) stations.push(next.fromStationId);
  }
  stations.push(legs[legs.length - 1]!.toStationId);
  return [...new Set(stations)];
}

export function journeyGraph(network: NetworkCacheData, legs: JourneyLeg[], journeyLabelsOnly = false): FeatureCollection<Point | LineString> {
  const features: FeatureCollection<Point | LineString>['features'] = [];
  const stations = new Map(network.stations.map(station => [station.id, station]));
  const included = new Set<string>();
  const named = new Set(journeyLabelStations(legs));
  const usedLines = new Map<string, Set<number>>();
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
      const lines = usedLines.get(station.id) ?? new Set<number>();
      lines.add(leg.lineId); usedLines.set(station.id, lines);
      if (previous) features.push({ type: 'Feature', geometry: { type: 'LineString',
        coordinates: segmentCoordinates(network, leg.routeId, previous, station)
      }, properties: { color: /^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(color) ? color : '#00636a',
        hasGeometry: !!findSegment(network, leg.routeId, previous.id, station.id) } });
      if (!included.has(station.id)) {
        included.add(station.id);
        const interchange = station.lines.filter(line => line.isOperational).length > 1;
        features.push({ type: 'Feature', geometry: { type: 'Point', coordinates: [station.longitude, station.latitude] },
          properties: { stationId: station.id, name: station.nameFa, named: named.has(station.id) || (!journeyLabelsOnly && interchange) } });
      }
      previous = station;
    }
  }
  for (const feature of features) {
    if (feature.geometry.type !== 'Point') continue;
    const labelColors = [...(usedLines.get(feature.properties!.stationId) ?? [])].sort((a, b) => a - b)
      .map(id => network.lines.find(line => line.id === id)?.color)
      .map(color => color && /^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(color) ? color : '#00636a');
    const colors = [...new Set(labelColors)];
    feature.properties = { ...feature.properties, labelColors: colors,
      labelImage: `journey-label-${colors.join('-')}` };
  }
  return { type: 'FeatureCollection', features };
}

export function addStreetPaths(graph: FeatureCollection<Point | LineString>, paths: (StreetPath | null | undefined)[]) {
  for (const path of paths) {
    if (path?.source === 'graphhopper' && path.coordinates.length > 1) graph.features.push({ type: 'Feature',
      geometry: { type: 'LineString', coordinates: path.coordinates },
      properties: { street: true, hasGeometry: true, walking: path.mode === 'walk', color: path.mode === 'walk' ? '#4285f4' : '#000000' } });
  }
  return graph;
}
