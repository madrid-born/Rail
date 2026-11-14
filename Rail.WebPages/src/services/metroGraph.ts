import type { FeatureCollection, LineString, Point } from 'geojson';
import type { NetworkCacheData } from './network';

export function buildMetroGraph(network: NetworkCacheData, hiddenLineIds: readonly number[] = []): FeatureCollection<Point | LineString> {
  const hidden = new Set(hiddenLineIds);
  const stations = new Map(network.stations.filter(station => station.lines.some(line => !hidden.has(line.lineId)) &&
    Number.isFinite(station.latitude) && Math.abs(station.latitude) <= 90
    && Number.isFinite(station.longitude) && Math.abs(station.longitude) <= 180
    && (station.latitude !== 0 || station.longitude !== 0)
  ).map(station => [station.id, station]));
  const colors = new Map(network.lines.map(line => [line.id,
    /^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(line.color) ? line.color : '#00636a']));
  const features: FeatureCollection<Point | LineString>['features'] = [];
  for (const station of stations.values()) {
    // Filtering controls visibility, while station identity keeps all its line colors.
    const activeLines = station.lines.filter(line => line.isOperational);
    const labelColors = [...new Set((activeLines.length ? activeLines : station.lines)
      .slice().sort((a, b) => a.lineId - b.lineId)
      .map(line => colors.get(line.lineId) ?? '#00636a'))];
    if (!labelColors.length) labelColors.push('#00636a');
    features.push({ type: 'Feature', geometry: { type: 'Point', coordinates: [station.longitude, station.latitude] },
      properties: { stationId: station.id, name: station.nameFa,
        interchange: activeLines.length > 1, labelColors,
        labelImage: `metro-label-${labelColors.join('-')}` } });
  }
  for (const route of network.routes) {
    if (hidden.has(route.lineId)) continue;
    const stops = [...route.stops].sort((a, b) => a.sequence - b.sequence);
    for (let i = 1; i < stops.length; i++) {
      const from = stations.get(stops[i - 1]!.stationId);
      const to = stations.get(stops[i]!.stationId);
      // Missing coordinates break a route rather than drawing a false shortcut.
      if (!from || !to || from.id === to.id) continue;
      features.push({ type: 'Feature', geometry: { type: 'LineString',
        coordinates: [[from.longitude, from.latitude], [to.longitude, to.latitude]] },
      properties: { routeId: route.id, lineId: route.lineId, color: colors.get(route.lineId) ?? '#00636a',
        operational: stops[i - 1]!.status === 'operational' && stops[i]!.status === 'operational' } });
    }
  }
  return { type: 'FeatureCollection', features };
}
