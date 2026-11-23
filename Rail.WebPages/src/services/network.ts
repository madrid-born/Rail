import { readonly, ref, shallowReadonly, shallowRef } from 'vue';
import { apiBase } from './api';

export interface MetroLine {
  id: number;
  nameFa: string;
  nameEn: string;
  color: string;
  infrastructureStatus?: string;
  routes: { id: string; branchCode: string | null }[];
}

export interface Station {
  id: string;
  nameFa: string;
  nameEn: string;
  latitude: number;
  longitude: number;
  infrastructureStatus: string;
  lines: { lineId: number; isOperational: boolean }[];
}

export interface RouteStop {
  stationId: string;
  sequence: number;
  status: string;
  nameFa: string;
  nameEn: string;
}

export interface MetroRoute {
  id: string;
  lineId: number;
  branchCode: string | null;
  stops: RouteStop[];
}

export interface NetworkCacheData {
  schemaVersion: 1;
  generatedAtUtc: string;
  lines: MetroLine[];
  stations: Station[];
  routes: MetroRoute[];
  segments?: { id: number; routeId: string; fromStationId: string; toStationId: string; coordinates: [number, number][] }[];
}

export interface CachedNetwork {
  data: NetworkCacheData;
  savedAtUtc: string;
  persisted: boolean;
  needsRefresh?: boolean;
}

const cacheOrigin = apiBase || 'same-origin';
// Refresh snapshots that still contain separate station records for expansion interchanges.
const cacheKey = `rail.network.v9:${cacheOrigin}`;
const legacyCacheKeys = ['v8', 'v7', 'v6', 'v5', 'v4', 'v3', 'v2'].map(version => `rail.network.${version}:${cacheOrigin}`);
let memoryCache: CachedNetwork | undefined;
let pendingCheck: Promise<boolean> | undefined;
const maxCacheAge = 24 * 60 * 60 * 1000;
const snapshot = shallowRef<CachedNetwork>();
const loading = ref(false);
const failed = ref(false);
const staleWarning = ref(false);

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null;
const isText = (value: unknown): value is string => typeof value === 'string';
const isId = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value);
const isBranch = (value: unknown) => value === null || isText(value);
const isDate = (value: unknown): value is string => isText(value) && Number.isFinite(Date.parse(value));

// Validate downloaded and stored data before letting it become the persistent cache.
function isSnapshot(value: unknown): value is NetworkCacheData {
  if (!isRecord(value) || value.schemaVersion !== 1 || !isDate(value.generatedAtUtc)
      || !Array.isArray(value.lines) || !Array.isArray(value.stations) || !Array.isArray(value.routes)) return false;
  return value.lines.every(line => isRecord(line) && isId(line.id) && isText(line.nameFa)
      && isText(line.nameEn) && isText(line.color) && Array.isArray(line.routes)
      && (line.infrastructureStatus === undefined || isText(line.infrastructureStatus))
      && line.routes.every((route: unknown) => isRecord(route) && isText(route.id) && isBranch(route.branchCode)))
    && value.stations.every(station => isRecord(station) && isText(station.id) && isText(station.nameFa)
      && typeof station.latitude === 'number' && Number.isFinite(station.latitude) && Math.abs(station.latitude) <= 90
      && typeof station.longitude === 'number' && Number.isFinite(station.longitude) && Math.abs(station.longitude) <= 180
      && isText(station.nameEn) && isText(station.infrastructureStatus) && Array.isArray(station.lines)
      && station.lines.every((line: unknown) => isRecord(line) && isId(line.lineId) && typeof line.isOperational === 'boolean'))
    && value.routes.every(route => isRecord(route) && isText(route.id) && isId(route.lineId)
      && isBranch(route.branchCode) && Array.isArray(route.stops)
      && route.stops.every((stop: unknown) => isRecord(stop) && isText(stop.stationId)
        && isId(stop.sequence) && stop.sequence > 0 && isText(stop.status) && isText(stop.nameFa) && isText(stop.nameEn)))
    && (value.segments === undefined || (Array.isArray(value.segments) && value.segments.every((segment: unknown) =>
      isRecord(segment) && isId(segment.id) && isText(segment.routeId) && isText(segment.fromStationId)
      && isText(segment.toStationId) && Array.isArray(segment.coordinates)
      && (segment.coordinates.length === 0 || segment.coordinates.length >= 2)
      && segment.coordinates.every((p: unknown) => Array.isArray(p) && p.length === 2
        && typeof p[0] === 'number' && Number.isFinite(p[0]) && Math.abs(p[0]) <= 180
        && typeof p[1] === 'number' && Number.isFinite(p[1]) && Math.abs(p[1]) <= 90))))
    && hasAllReferences(value as unknown as NetworkCacheData);
}

function hasAllReferences(data: NetworkCacheData) {
  const lines = new Set(data.lines.map(line => line.id));
  const stations = new Set(data.stations.map(station => station.id));
  const routes = new Map(data.routes.map(route => [route.id, route]));
  return data.lines.every(line => line.routes.every(route => routes.get(route.id)?.lineId === line.id))
    && data.stations.every(station => station.lines.every(line => lines.has(line.lineId)))
    && data.routes.every(route => lines.has(route.lineId) && route.stops.every(stop => stations.has(stop.stationId)))
    && (data.segments ?? []).every(segment => routes.has(segment.routeId)
      && stations.has(segment.fromStationId) && stations.has(segment.toStationId));
}

function withLineStatuses(data: NetworkCacheData): NetworkCacheData {
  const lines = data.lines.map(line => {
    if (line.infrastructureStatus !== undefined) return line;
    // Older servers include stop statuses even when they omit line status.
    const stops = data.routes.filter(route => route.lineId === line.id).flatMap(route => route.stops);
    if (!stops.length || !stops.every(stop => stop.status === 'under_construction' || stop.status === 'planned')) return line;
    const infrastructureStatus = stops.some(stop => stop.status === 'under_construction') ? 'under_construction' : 'planned';
    return { ...line, infrastructureStatus };
  });
  return { ...data, lines };
}

function readCache(): CachedNetwork | undefined {
  for (const key of [cacheKey, ...legacyCacheKeys]) {
    try {
      const raw = localStorage.getItem(key);
      if (!raw) continue;
      const saved: unknown = JSON.parse(raw);
      if (isRecord(saved) && isDate(saved.savedAtUtc) && isSnapshot(saved.data)) {
        return { data: withLineStatuses(saved.data), savedAtUtc: saved.savedAtUtc, persisted: true,
          needsRefresh: key !== cacheKey };
      }
      // Leave even invalid old bytes in place until a complete replacement is saved.
    } catch { /* Try any older cache when an entry is unavailable or invalid. */ }
  }
}

async function downloadSnapshot(): Promise<CachedNetwork> {
  const request = new AbortController();
  const timeout = setTimeout(() => request.abort(), 15_000);
  try {
    const response = await fetch(`${apiBase}/api/network/load-cached-data`, {
      signal: request.signal, cache: 'no-store', headers: { Accept: 'application/json' },
    });
    if (!response.ok) throw new Error(`Rail API returned HTTP ${response.status}`);
    const received: unknown = await response.json();
    if (!isSnapshot(received)) throw new Error('The network cache response is incomplete or unsupported.');
    const data = withLineStatuses(received);
    const snapshot: CachedNetwork = { data, savedAtUtc: new Date().toISOString(), persisted: false };
    try {
      localStorage.setItem(cacheKey, JSON.stringify({ data, savedAtUtc: snapshot.savedAtUtc }));
      snapshot.persisted = true;
    } catch { /* Keep the old disk copy and use the complete new snapshot for this session. */ }
    memoryCache = snapshot;
    return snapshot;
  } finally {
    clearTimeout(timeout);
  }
}

export function checkNetworkCache(forceRefresh = false): Promise<boolean> {
  // Called on app startup/resume, never when expanding a line or changing tabs.
  if (pendingCheck) return pendingCheck;
  // Another app tab may have saved a newer API snapshot.
  const stored = readCache();
  const useStored = stored && (!memoryCache || (memoryCache.needsRefresh && !stored.needsRefresh)
    || (!!stored.needsRefresh === !!memoryCache.needsRefresh && Date.parse(stored.savedAtUtc) > Date.parse(memoryCache.savedAtUtc)));
  const cached = useStored ? stored : memoryCache;
  if (cached) {
    memoryCache = cached;
    snapshot.value = cached;
  }
  const age = cached ? Date.now() - Date.parse(cached.savedAtUtc) : undefined;
  // A future timestamp is also rechecked, in case the device clock moved backwards.
  if (!forceRefresh && age !== undefined && age >= 0 && age <= maxCacheAge
    && !cached?.needsRefresh && cached?.data.segments !== undefined) return Promise.resolve(true);

  loading.value = true;
  failed.value = false;
  staleWarning.value = false;
  pendingCheck = downloadSnapshot().then(data => {
    snapshot.value = data;
    return true;
  }).catch(() => {
    failed.value = !cached;
    // Missing data has its own empty/error state; expired cached data warns.
    staleWarning.value = age !== undefined && age > maxCacheAge;
    return false;
  }).finally(() => {
    loading.value = false;
    pendingCheck = undefined;
  });
  return pendingCheck;
}

export function useNetworkCache() {
  return {
    snapshot: shallowReadonly(snapshot),
    loading: readonly(loading),
    failed: readonly(failed),
    staleWarning: readonly(staleWarning),
    dismissWarning: () => { staleWarning.value = false; },
  };
}
