import { readonly, ref, shallowReadonly, shallowRef } from 'vue';
import { apiBase } from './api';

export interface TimetableTrip {
  id: number;
  routeId: string;
  lineId: number;
  directionStationId: string;
  position: { station: string | null; fromStation: string | null; toStation: string | null };
}

interface TimetableSnapshot {
  requestedAtUtc: string;
  trips: TimetableTrip[];
}

const cacheKey = `rail.timetable.v1:${apiBase || 'same-origin'}`;
const snapshot = shallowRef<TimetableSnapshot>();
const failed = ref(false);
let initialized = false;
let pageActive = false;
let timer: ReturnType<typeof setTimeout> | undefined;
let request: AbortController | undefined;
let requestedMinute: number | undefined;

function isTrips(value: unknown): value is TimetableTrip[] {
  const record = (item: unknown): item is Record<string, unknown> => typeof item === 'object' && item !== null;
  const station = (item: unknown) => item === null || typeof item === 'string';
  return Array.isArray(value) && value.every(trip => record(trip)
    && Number.isSafeInteger(trip.id) && Number.isInteger(trip.lineId)
    && typeof trip.routeId === 'string' && typeof trip.directionStationId === 'string'
    && record(trip.position) && station(trip.position.station)
    && station(trip.position.fromStation) && station(trip.position.toStation));
}

function restore() {
  try {
    const raw = localStorage.getItem(cacheKey);
    if (!raw) return;
    const cached = JSON.parse(raw);
    if (cached.schemaVersion === 1 && typeof cached.requestedAtUtc === 'string'
        && Number.isFinite(Date.parse(cached.requestedAtUtc)) && isTrips(cached.trips)) {
      snapshot.value = { requestedAtUtc: cached.requestedAtUtc, trips: cached.trips };
    }
  } catch { /* First successful response replaces missing/unusable storage. */ }
}

async function refresh() {
  const minute = Math.floor(Date.now() / 60_000);
  if (request && requestedMinute === minute) return;
  // A new minute supersedes a still-pending response from the previous minute.
  request?.abort();
  const controller = new AbortController();
  request = controller;
  requestedMinute = minute;
  // Query the intended minute exactly, including zero milliseconds, so zero-dwell
  // station arrivals in the minute-resolution timetable are represented consistently.
  const requestedAtUtc = new Date(minute * 60_000).toISOString();
  const timeout = setTimeout(() => controller.abort(), 45_000);
  try {
    const response = await fetch(`${apiBase}/api/timetable/TimeTableCache?dateTime=${encodeURIComponent(requestedAtUtc)}`, {
      signal: controller.signal, cache: 'no-store', headers: { Accept: 'application/json' },
    });
    if (!response.ok) throw new Error(`Timetable HTTP ${response.status}`);
    const trips: unknown = await response.json();
    if (!isTrips(trips)) throw new Error('Invalid timetable response');
    if (request !== controller || controller.signal.aborted) return;
    const replacement = { requestedAtUtc, trips };
    try {
      localStorage.setItem(cacheKey, JSON.stringify({ schemaVersion: 1, ...replacement }));
    } catch { /* Keep the complete new session copy without clearing the old disk copy. */ }
    snapshot.value = replacement;
    failed.value = false;
  } catch {
    if (request === controller) failed.value = true;
  } finally {
    clearTimeout(timeout);
    if (request === controller) request = undefined;
  }
}

function clearTimer() {
  if (timer !== undefined) clearTimeout(timer);
  timer = undefined;
}

function scheduleNextMinute() {
  clearTimer();
  if (!pageActive || document.visibilityState !== 'visible') return;
  // Re-align on every tick, rather than drifting by a minute after each response.
  timer = setTimeout(() => {
    timer = undefined;
    if (!pageActive || document.visibilityState !== 'visible') return;
    scheduleNextMinute();
    void refresh();
  }, 60_000 - Date.now() % 60_000);
}

export function initializeTimetableCache() {
  if (initialized) return;
  initialized = true;
  restore();
  // Every app launch requests current positions, regardless of the saved copy's age.
  void refresh();
}

export function startTimetablePolling() {
  initializeTimetableCache();
  pageActive = true;
  if (requestedMinute !== Math.floor(Date.now() / 60_000) || failed.value) void refresh();
  scheduleNextMinute();
}

export function stopTimetablePolling() {
  pageActive = false;
  clearTimer();
}

export function onTimetableVisibilityChange() {
  clearTimer();
  if (document.visibilityState === 'visible') {
    void refresh();
    scheduleNextMinute();
  } else {
    // Hidden WebViews throttle timers; resume with a new request and a clean boundary.
    const previous = request;
    request = undefined;
    previous?.abort();
  }
}

export function useTimetableCache() {
  return { snapshot: shallowReadonly(snapshot), failed: readonly(failed) };
}
