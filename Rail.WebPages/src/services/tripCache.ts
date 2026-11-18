import type { WalkingJourneyResult } from './journey';

interface CachedTrip {
  response: WalkingJourneyResult;
  selectedChoiceId?: string;
  savedAt: number;
}
const trips = new Map<string, CachedTrip>();
const refreshAfter = 30_000;

export function cachedTrip(key: string): CachedTrip | undefined {
  const entry = trips.get(key);
  if (entry) { trips.delete(key); trips.set(key, entry); }
  return entry;
}
export function isTripFresh(key: string, now = Date.now()): boolean {
  const entry = trips.get(key);
  return !!entry && now - entry.savedAt >= 0 && now - entry.savedAt < refreshAfter;
}
export function cacheTrip(key: string, response: WalkingJourneyResult, selectedChoiceId?: string, now = Date.now()) {
  trips.delete(key);
  if (trips.size >= 8) trips.delete(trips.keys().next().value!);
  trips.set(key, { response, selectedChoiceId, savedAt: now });
}
export function rememberTripChoice(key: string, id: string | undefined) {
  const entry = trips.get(key);
  if (entry) entry.selectedChoiceId = id;
}
