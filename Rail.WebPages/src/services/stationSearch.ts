import type { Station } from './network';

export function normalizeStationSearch(value: string): string {
  return value.normalize('NFKD').replace(/\p{M}/gu, '').toLowerCase()
    .replace(/[يى]/g, 'ی').replace(/ك/g, 'ک')
    .replace(/[۰-۹]/g, digit => String(digit.charCodeAt(0) - 0x06f0))
    .replace(/[٠-٩]/g, digit => String(digit.charCodeAt(0) - 0x0660))
    .replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
}

export function stationSearchWords(query: string): string[] {
  return normalizeStationSearch(query).split(/\s+/).filter(Boolean);
}

const isSearchableStation = (station: Station) =>
  station.infrastructureStatus !== 'under_construction' && station.infrastructureStatus !== 'planned';

export function searchStations(stations: readonly Station[], words: readonly string[], includeUnbuilt = false): Station[] {
  return stations.filter(station => {
    if (!includeUnbuilt && !isSearchableStation(station)) return false;
    const names = [station.nameFa, station.nameEn].map(normalizeStationSearch);
    return words.every(word => names.some(name => name.includes(word) || name.replace(/\s/g, '').includes(word)));
  });
}

function editDistance(a: string, b: string): number {
  let row = Array.from({ length: b.length + 1 }, (_, i) => i);
  for (let i = 0; i < a.length; i++) {
    const next = [i + 1];
    for (let j = 0; j < b.length; j++) {
      next.push(Math.min(row[j + 1]! + 1, next[j]! + 1, row[j]! + Number(a[i] !== b[j])));
    }
    row = next;
  }
  return row[b.length]!;
}

function stationMatchScore(query: string, name: string): number {
  if (query === name) return 4;
  if (name.startsWith(query)) return 2 + query.length / name.length;
  if (query.length >= name.length * 2) return 0;
  const full = 1 - editDistance(query, name) / Math.max(query.length, name.length);
  const prefix = query.length <= name.length ? 1 - editDistance(query, name.slice(0, query.length)) / query.length : 0;
  return full > 0.5 || prefix >= 0.8 ? Math.max(full, prefix) + full / 10 : 0;
}

export function searchMapStations(stations: readonly Station[], value: string, center?: readonly [number, number]): Station[] {
  const query = normalizeStationSearch(value).replaceAll(' ', '');
  if (query.length < 2) return [];
  const lonScale = center ? Math.cos(center[1] * Math.PI / 180) : 1;
  return stations.filter(isSearchableStation).map(station => {
    const names = [station.nameFa, station.nameEn].map(name => normalizeStationSearch(name).replaceAll(' ', ''));
    const score = Math.max(...names.map(name => stationMatchScore(query, name)));
    const distance = center ? ((station.longitude - center[0]) * lonScale) ** 2 + (station.latitude - center[1]) ** 2 : 0;
    return { station, score, distance };
  }).filter(match => match.score > 0)
    .sort((a, b) => b.score - a.score || a.distance - b.distance || a.station.nameFa.localeCompare(b.station.nameFa, 'fa'))
    .slice(0, 3).map(match => match.station);
}
