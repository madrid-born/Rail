export interface MetroLine {
  id: number;
  nameFa: string;
  nameEn: string;
  color: string;
  routes: { id: string; branchCode: string | null }[];
}

export interface Station {
  id: string;
  nameFa: string;
  nameEn: string;
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
}

export interface CachedNetwork {
  data: NetworkCacheData;
  savedAtUtc: string;
  persisted: boolean;
}

const apiBase = (import.meta.env.VITE_API_BASE_URL
  ?? (import.meta.env.DEV ? '' : 'https://localhost:7130')).replace(/\/+$/, '');
const cacheKey = `rail.network.v1:${apiBase || 'same-origin'}`;
let memoryCache: CachedNetwork | undefined;
let pendingLoad: Promise<CachedNetwork> | undefined;

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
      && line.routes.every((route: unknown) => isRecord(route) && isText(route.id) && isBranch(route.branchCode)))
    && value.stations.every(station => isRecord(station) && isText(station.id) && isText(station.nameFa)
      && isText(station.nameEn) && isText(station.infrastructureStatus) && Array.isArray(station.lines)
      && station.lines.every((line: unknown) => isRecord(line) && isId(line.lineId) && typeof line.isOperational === 'boolean'))
    && value.routes.every(route => isRecord(route) && isText(route.id) && isId(route.lineId)
      && isBranch(route.branchCode) && Array.isArray(route.stops)
      && route.stops.every((stop: unknown) => isRecord(stop) && isText(stop.stationId)
        && isId(stop.sequence) && stop.sequence > 0 && isText(stop.status) && isText(stop.nameFa) && isText(stop.nameEn)))
    && hasAllReferences(value as unknown as NetworkCacheData);
}

function hasAllReferences(data: NetworkCacheData) {
  const lines = new Set(data.lines.map(line => line.id));
  const stations = new Set(data.stations.map(station => station.id));
  const routes = new Map(data.routes.map(route => [route.id, route]));
  return data.lines.every(line => line.routes.every(route => routes.get(route.id)?.lineId === line.id))
    && data.stations.every(station => station.lines.every(line => lines.has(line.lineId)))
    && data.routes.every(route => lines.has(route.lineId) && route.stops.every(stop => stations.has(stop.stationId)));
}

function readCache(): CachedNetwork | undefined {
  try {
    const raw = localStorage.getItem(cacheKey);
    if (raw) {
      const saved: unknown = JSON.parse(raw);
      if (isRecord(saved) && isDate(saved.savedAtUtc) && isSnapshot(saved.data)) {
        return { data: saved.data, savedAtUtc: saved.savedAtUtc, persisted: true };
      }
      localStorage.removeItem(cacheKey);
    }
  } catch { /* Corrupt or unavailable storage is a cache miss. */ }
}

async function downloadSnapshot(): Promise<CachedNetwork> {
  const request = new AbortController();
  const timeout = setTimeout(() => request.abort(), 15_000);
  try {
    const response = await fetch(`${apiBase}/api/network/load-cached-data`, {
      signal: request.signal, cache: 'no-store', headers: { Accept: 'application/json' },
    });
    if (!response.ok) throw new Error(`Rail API returned HTTP ${response.status}`);
    const data: unknown = await response.json();
    if (!isSnapshot(data)) throw new Error('The network cache response is incomplete or unsupported.');
    const snapshot: CachedNetwork = { data, savedAtUtc: new Date().toISOString(), persisted: false };
    try {
      localStorage.setItem(cacheKey, JSON.stringify({ data, savedAtUtc: snapshot.savedAtUtc }));
      snapshot.persisted = true;
    } catch { /* Keep a session copy and let the page explain that persistence failed. */ }
    memoryCache = snapshot;
    return snapshot;
  } finally {
    clearTimeout(timeout);
  }
}

export function loadCachedData(refresh = false): Promise<CachedNetwork> {
  // One shared download survives page navigation and saves the first snapshot.
  if (pendingLoad) return pendingLoad;
  if (!refresh) {
    const cached = memoryCache ?? readCache();
    if (cached) {
      memoryCache = cached;
      return Promise.resolve(cached);
    }
  }
  // A failed refresh never replaces the previous working memory/disk snapshot.
  pendingLoad = downloadSnapshot().finally(() => { pendingLoad = undefined; });
  return pendingLoad;
}
