import type { FeatureCollection, LineString, Point } from 'geojson';
import type { NetworkCacheData, Station } from './network';

export function hasStationCoordinates(station: Station) {
  return Number.isFinite(station.latitude) && Math.abs(station.latitude) <= 90
    && Number.isFinite(station.longitude) && Math.abs(station.longitude) <= 180
    && (station.latitude !== 0 || station.longitude !== 0);
}

export function metroLineColors(lines: NetworkCacheData['lines']) {
  return new Map(lines.map(line => [line.id,
    /^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(line.color) ? line.color : '#00636a']));
}

export function stationLabelStyle(station: Station, colors: ReadonlyMap<number, string>) {
  const activeLines = station.lines.filter(line => line.isOperational);
  const labelColors = [...new Set((activeLines.length ? activeLines : station.lines)
    .slice().sort((a, b) => a.lineId - b.lineId)
    .map(line => colors.get(line.lineId) ?? '#00636a'))];
  if (!labelColors.length) labelColors.push('#00636a');
  const labelDashed = activeLines.length === 0 && station.infrastructureStatus === 'under_construction';
  return { labelColors, labelDashed, interchange: activeLines.length > 1 };
}

const isUnbuilt = (status?: string) => status === 'under_construction' || status === 'planned';

export function buildMetroGraph(network: NetworkCacheData, hiddenLineIds: readonly number[] = [],
  showUnbuiltStations = true): FeatureCollection<Point | LineString> {
  const unbuiltLines = new Set(network.lines.filter(line => isUnbuilt(line.infrastructureStatus)).map(line => line.id));
  const hidden = new Set(showUnbuiltStations ? hiddenLineIds : [...hiddenLineIds, ...unbuiltLines]);
  const stations = new Map(network.stations.map(station => [station.id, station]));
  const colors = metroLineColors(network.lines);
  const features: FeatureCollection<Point | LineString>['features'] = [];
  for (const station of stations.values()) {
    const unbuilt = isUnbuilt(station.infrastructureStatus);
    const requireOperatingLine = !showUnbuiltStations && station.infrastructureStatus === 'operational';
    if ((unbuilt && !showUnbuiltStations) || !hasStationCoordinates(station)
      || !station.lines.some(line => !hidden.has(line.lineId) && (!requireOperatingLine || line.isOperational))) continue;
    // Filtering controls visibility, while station identity keeps all its line colors.
    const { labelColors, labelDashed, interchange } = stationLabelStyle(station, colors);
    features.push({ type: 'Feature', geometry: { type: 'Point', coordinates: [station.longitude, station.latitude] },
      properties: { stationId: station.id, name: station.nameFa,
        interchange, labelColors, labelDashed, unbuilt,
        labelImage: `metro-label-${labelColors.join('-')}${labelDashed ? '-dashed' : ''}` } });
  }
  for (const route of network.routes) {
    if (hidden.has(route.lineId)) continue;
    const unbuiltLine = unbuiltLines.has(route.lineId);
    const stops = [...route.stops].sort((a, b) => a.sequence - b.sequence);
    for (let i = 1; i < stops.length; i++) {
      const fromStop = stops[i - 1]!;
      const toStop = stops[i]!;
      const from = stations.get(fromStop.stationId);
      const to = stations.get(toStop.stationId);
      if (!from || !to || from.id === to.id) continue;
      const segment = findSegment(network, route.id, from.id, to.id, true);
      const hasGeometry = !!segment && segment.coordinates.length >= 2;
      if (!hasGeometry && (!hasStationCoordinates(from) || !hasStationCoordinates(to))) continue;
      // Track status is independent of station status. Older snapshots only have geometry and stop metadata.
      const unbuiltTrack = segment?.status !== undefined ? isUnbuilt(segment.status) : !hasGeometry
        && (isUnbuilt(fromStop.status) || isUnbuilt(toStop.status)
          || isUnbuilt(from.infrastructureStatus) || isUnbuilt(to.infrastructureStatus));
      const unbuilt = unbuiltLine || unbuiltTrack;
      if (unbuilt && !showUnbuiltStations) continue;
      features.push({ type: 'Feature', geometry: { type: 'LineString',
        coordinates: coordinatesForSegment(hasGeometry ? segment : undefined, from, to) },
      properties: { routeId: route.id, lineId: route.lineId, color: colors.get(route.lineId) ?? '#00636a',
        hasGeometry, unbuilt,
        operational: !unbuilt && fromStop.status === 'operational' && toStop.status === 'operational' } });
    }
  }
  return { type: 'FeatureCollection', features };
}

export function segmentCoordinates(network: NetworkCacheData, routeId: string,
  from: { id: string; longitude: number; latitude: number }, to: { id: string; longitude: number; latitude: number }): [number, number][] {
  return coordinatesForSegment(findSegment(network, routeId, from.id, to.id), from, to);
}

type TrackSegment = NonNullable<NetworkCacheData['segments']>[number];

function coordinatesForSegment(segment: TrackSegment | undefined,
  from: { id: string; longitude: number; latitude: number }, to: { longitude: number; latitude: number }): [number, number][] {
  if (segment) return segment.fromStationId === from.id ? segment.coordinates : [...segment.coordinates].reverse();
  return [[from.longitude, from.latitude], [to.longitude, to.latitude]];
}

export function findSegment(network: NetworkCacheData, routeId: string, fromId: string, toId: string,
  includeWithoutGeometry = false) {
  return network.segments?.find(segment => segment.routeId === routeId && (includeWithoutGeometry || segment.coordinates.length >= 2) &&
    ((segment.fromStationId === fromId && segment.toStationId === toId) ||
     (segment.fromStationId === toId && segment.toStationId === fromId)));
}
