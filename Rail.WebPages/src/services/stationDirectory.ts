import type { NetworkCacheData } from './network';

export function stationDirectory(network: NetworkCacheData, showUnbuiltStations: boolean) {
  const { lines, routes, stations } = network;
  if (showUnbuiltStations) return { lines, routes, stations };

  const operatingLines = lines.filter(line => line.infrastructureStatus === undefined || line.infrastructureStatus === 'operational');
  const lineIds = new Set(operatingLines.map(line => line.id));
  const operatingStations = stations.filter(station => station.infrastructureStatus === 'operational');
  const memberships = new Map(operatingStations.map(station => [station.id,
    new Set(station.lines.filter(line => line.isOperational && lineIds.has(line.lineId)).map(line => line.lineId))]));

  const operatingRoutes = routes.filter(route => lineIds.has(route.lineId)).map(route => ({
    ...route,
    stops: route.stops.filter(stop => stop.status === 'operational' && memberships.get(stop.stationId)?.has(route.lineId)),
  })).filter(route => route.branchCode
    ? new Set(route.stops.map(stop => stop.stationId)).size >= 2
    : route.stops.length > 0);

  const servedLines = new Map<string, Set<number>>();
  for (const route of operatingRoutes) {
    for (const stop of route.stops) {
      const served = servedLines.get(stop.stationId) ?? new Set<number>();
      served.add(route.lineId);
      servedLines.set(stop.stationId, served);
    }
  }

  return {
    lines: operatingLines,
    routes: operatingRoutes,
    stations: operatingStations.filter(station => servedLines.has(station.id)).map(station => ({
      ...station,
      lines: station.lines.filter(line => servedLines.get(station.id)?.has(line.lineId)),
    })),
  };
}
