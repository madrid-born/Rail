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

export function searchStations(stations: readonly Station[], words: readonly string[]): Station[] {
  return stations.filter(station => {
    const names = [station.nameFa, station.nameEn].map(normalizeStationSearch);
    return words.every(word => names.some(name => name.includes(word) || name.replace(/\s/g, '').includes(word)));
  });
}
