import indexUrl from 'virtual:rail-map-search-index';
import type { MapPoint } from './journey';
import { normalizeStationSearch } from './stationSearch';

export type MapPlaceKind = 'locality' | 'neighborhood' | 'bus' | 'street' | 'landmark';
export type MapSearchKind = MapPlaceKind | 'station';
export type MapSearchEntry = [name: string, kind: MapPlaceKind, rank: number, aliases: string[], coordinates: number[]];
export type MapStationResult = { name: string; point: MapPoint; kind: 'station'; stationId: string; labelColors: string[]; labelDashed: boolean };
type MapPlaceResult = { name: string; point: MapPoint; kind: MapPlaceKind; area?: string };
export type MapSearchResult = MapPlaceResult | MapStationResult;
type IndexedEntry = { name: string; kind: MapPlaceKind; rank: number; names: string[]; compact: string[]; key: string; coordinates: number[] };
type Match = { entry: IndexedEntry; point: MapPoint; score: number; distance: number };
type SearchIndex = { entries: IndexedEntry[]; areas: IndexedEntry[] };

let pending: Promise<SearchIndex> | undefined;

export function loadMapSearch() {
  pending ??= (async () => {
    if (!indexUrl) throw new Error('The map search index was not built.');
    const response = await fetch(indexUrl);
    if (!response.ok || !response.body) throw new Error(`Map search index returned ${response.status}.`);
    const entries: MapSearchEntry[] = await new Response(response.body.pipeThrough(new DecompressionStream('gzip'))).json();
    const indexed = entries.map(([name, kind, rank, aliases, coordinates]): IndexedEntry => {
      const names = [...new Set([name, ...aliases].map(normalizeStationSearch).filter(Boolean))];
      const compact = names.map(value => value.replaceAll(' ', ''));
      // Street labels can include a segment description in the same name.
      const base = kind === 'street' ? name.split(/\s+[-–—]\s*/u)[0]! : name;
      const key = normalizeStationSearch(base).replaceAll(' ', '') || compact[0] || name;
      return { name, kind, rank, names, compact, key, coordinates };
    });
    return { entries: indexed, areas: indexed.filter(entry => entry.kind === 'locality' || entry.kind === 'neighborhood') };
  })().catch(error => { pending = undefined; throw error; });
  return pending;
}

function matchScore(entry: IndexedEntry, query: string, compact: string, words: string[]) {
  if (entry.names[0] === query || entry.compact[0] === compact) return 0;
  if (entry.names.includes(query) || entry.compact.includes(compact)) return 1;
  if (entry.names.some(name => name.startsWith(query)) || entry.compact.some(name => name.startsWith(compact))) return 4;
  if (entry.names.some(name => words.every(word => name.startsWith(word) || name.includes(` ${word}`)))) return 5;
  if (entry.compact.some(name => name.includes(compact)) || entry.names.some(name => words.every(word => name.includes(word)))) return 10;
  return Infinity;
}

const compareMatches = (a: Match, b: Match) => a.score - b.score || a.distance - b.distance;

function nearbyArea(areas: readonly IndexedEntry[], match: Match): string | undefined {
  let best = 0.025 ** 2;
  let closest: string | undefined;
  for (const area of areas) {
    if (area.key === match.entry.key) continue;
    for (let i = 0; i < area.coordinates.length; i += 2) {
      const distance = ((area.coordinates[i]! - match.point[0]) * 0.81) ** 2 + (area.coordinates[i + 1]! - match.point[1]) ** 2;
      if (distance >= best) continue;
      best = distance;
      closest = area.name;
    }
  }
  return closest;
}

export function searchMap(index: SearchIndex, query: string, center?: MapPoint, stations: readonly MapStationResult[] = []): MapPlaceResult[] {
  const normalized = normalizeStationSearch(query);
  const compact = normalized.replaceAll(' ', '');
  if (compact.length < 2) return [];
  const words = normalized.split(' ');
  const matches: Match[] = [];
  const streets = new Map<string, Match>();
  const lonScale = center ? Math.cos(center[1] * Math.PI / 180) : 1;
  for (const entry of index.entries) {
    const score = matchScore(entry, normalized, compact, words);
    if (!Number.isFinite(score)) continue;
    for (let i = 0; i < entry.coordinates.length; i += 2) {
      const lon = entry.coordinates[i]!, lat = entry.coordinates[i + 1]!;
      const distance = center ? ((lon - center[0]) * lonScale) ** 2 + (lat - center[1]) ** 2 : 0;
      // Let nearby landmarks compete with distant same-name streets while keeping regional matches searchable.
      const distancePenalty = Math.min(5, Math.sqrt(distance) * 100);
      const match: Match = { entry, point: [lon, lat], score: score + entry.rank + distancePenalty, distance };
      if (entry.kind === 'street') {
        const previous = streets.get(entry.key);
        if (!previous || compareMatches(match, previous) < 0) streets.set(entry.key, match);
      } else {
        matches.push(match);
      }
    }
  }
  matches.push(...streets.values());
  matches.sort(compareMatches);
  const results: Match[] = [];
  const stationNames = stations.map(station => ({ key: normalizeStationSearch(station.name).replaceAll(' ', ''), point: station.point }));
  for (const match of matches) {
    const sameName = results.filter(other => other.entry.key === match.entry.key);
    const sameKind = sameName.filter(other => other.entry.kind === match.entry.kind);
    const duplicate = sameKind.some(other => Math.abs(other.point[0] - match.point[0]) < 0.003 && Math.abs(other.point[1] - match.point[1]) < 0.003);
    const nearStation = match.entry.kind === 'bus' && stationNames.some(other => other.key === match.entry.key
      && Math.abs(other.point[0] - match.point[0]) < 0.002 && Math.abs(other.point[1] - match.point[1]) < 0.002);
    if (!duplicate && !nearStation && sameKind.length < 2) results.push(match);
    if (results.length === 8) break;
  }
  return results.map(match => ({ name: match.entry.name, kind: match.entry.kind, point: match.point, area: nearbyArea(index.areas, match) }));
}
