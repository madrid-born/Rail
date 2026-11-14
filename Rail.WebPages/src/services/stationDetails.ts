import type { MetroRoute, Station } from './network';
import { apiBase } from './api';

export interface StationDetails extends Station {
  latitude: number;
  longitude: number;
  amenities: { type: number; isAvailable: boolean; isVerified: boolean }[];
  entrances: { code: string; nameFa: string; nameEn: string | null; addressFa: string | null;
    addressEn: string | null; latitude: number | null; longitude: number | null; isStepFree: boolean | null }[];
}

const preferences = new Map<string, number>();
const preferenceKey = (stationId: string) => `rail.station.line.v1:${encodeURIComponent(stationId)}`;

export function rememberedStationLine(stationId: string): number | undefined {
  let saved = preferences.get(stationId);
  try {
    const raw = localStorage.getItem(preferenceKey(stationId));
    if (raw !== null && saved === undefined) saved = Number(raw);
  } catch { /* Session preferences still work when storage is unavailable. */ }
  return saved !== undefined && Number.isInteger(saved) && saved > 0 && saved <= 255 ? saved : undefined;
}

export function preferredStationLine(stationId: string, lineIds: number[]): number | undefined {
  const saved = rememberedStationLine(stationId);
  return saved !== undefined && lineIds.includes(saved) ? saved : [...lineIds].sort((a, b) => a - b)[0];
}

export function saveStationLine(stationId: string, lineId: number) {
  preferences.set(stationId, lineId);
  try { localStorage.setItem(preferenceKey(stationId), String(lineId)); } catch { /* Use the session copy. */ }
}

export function stationWindow(route: MetroRoute, stationId: string) {
  const stops = [...route.stops].sort((a, b) => a.sequence - b.sequence);
  const index = stops.findIndex(stop => stop.stationId === stationId);
  if (index < 0) return { start: 0, stops: [] };
  const start = Math.max(0, Math.min(index - 2, stops.length - 5));
  return { start, stops: stops.slice(start, start + 5) };
}

export async function fetchStationDetails(stationId: string, signal: AbortSignal): Promise<StationDetails> {
  const response = await fetch(`${apiBase}/api/stations/${encodeURIComponent(stationId)}`, {
    signal, headers: { Accept: 'application/json' },
  });
  if (!response.ok) throw new Error(`Station details HTTP ${response.status}`);
  return response.json();
}
