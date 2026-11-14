import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import * as vue from 'vue';
import ts from 'typescript';

const source = await readFile(new URL('../src/services/stationPage.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
  .replace("import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue';", 'const { onBeforeUnmount, onMounted, ref, shallowRef, watch } = globalThis.stationTestVue;')
  .replace("import { apiBase } from './api';", "const apiBase = '';")
  .replace("import { preferredStationLine, rememberedStationLine, saveStationLine } from './stationDetails';", 'const { preferredStationLine, rememberedStationLine, saveStationLine } = globalThis.stationTestPreferences;')
  .replace("import { useNetworkCache } from './network';", 'const useNetworkCache = () => ({ snapshot: globalThis.stationTestCaches.network });')
  .replace("import { useTimetableCache } from './timetable';", 'const useTimetableCache = () => ({ snapshot: globalThis.stationTestCaches.timetable });');
let instance = 0;
const settle = async () => { await new Promise(resolve => setImmediate(resolve)); await vue.nextTick(); };
const board = (lineId = 3, stationId = 'theatre') => ({ stationId, lineId, trips: [], nextArrivals: [], requestedAt: '2026-11-14T10:00:00+03:30' });
const page = (line = 3, id = 'theatre') => ({ station: { id }, selectedLineId: line,
  lines: [{ id: 3 }, { id: 4 }], routes: [], stations: [], board: board(line, id), boardUnavailable: false });
const json = value => new Response(JSON.stringify(value));
async function setup(t, handler = async () => json(page()), saved, caches = {}) {
  const mounts = [], unmounts = [], requests = [], preferences = new Map();
  const listeners = new Map();
  globalThis.stationTestVue = { ...vue, onMounted: fn => mounts.push(fn), onBeforeUnmount: fn => unmounts.push(fn) };
  globalThis.stationTestPreferences = { rememberedStationLine: id => preferences.get(id) ?? saved,
    preferredStationLine: (id, ids) => ids.includes(preferences.get(id) ?? saved) ? preferences.get(id) ?? saved : [...ids].sort((a, b) => a - b)[0],
    saveStationLine: (id, line) => preferences.set(id, line) };
  globalThis.stationTestCaches = { network: vue.shallowRef(caches.network), timetable: vue.shallowRef(caches.timetable) };
  globalThis.document = { visibilityState: 'visible', addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) };
  globalThis.fetch = async (url, init) => { requests.push({ url, init }); return handler(url, init); };
  const module = await import(`data:text/javascript;base64,${Buffer.from(compiled + `\n// instance ${instance++}`).toString('base64')}`);
  const scope = vue.effectScope();
  const id = vue.ref('theatre');
  const state = scope.run(() => module.useStationPage(id));
  mounts.forEach(fn => fn());
  t.after(() => { unmounts.forEach(fn => fn()); scope.stop(); });
  await settle();
  return { ...state, id, requests, preferences, listeners, createPreview: module.createStationPreview };
}

test('initial station load is exactly one API request, including the initial train board', async t => {
  const state = await setup(t);
  assert.deepEqual(state.requests.map(request => request.url), ['/api/stations/theatre/page']);
  assert.equal(state.selectedLineId.value, 3);
  assert.equal(state.board.value.lineId, 3);
  assert.equal(state.loading.value, false);
  await settle();
  assert.equal(state.requests.length, 1);
});

test('saved line selection is sent with the single initial request', async t => {
  const state = await setup(t, async () => json(page(4)), 4);
  assert.deepEqual(state.requests.map(request => request.url), ['/api/stations/theatre/page?lineId=4']);
  assert.equal(state.selectedLineId.value, 4);
});

test('switching a line requests only its board and reuses station metadata', async t => {
  const state = await setup(t, async url => json(url.includes('/page') ? page() : board(4)));
  state.selectLine(4);
  await settle();
  assert.deepEqual(state.requests.map(request => request.url), ['/api/stations/theatre/page', '/api/timetable/stations/theatre/board?lineId=4']);
  assert.equal(state.preferences.get('theatre'), 4);
  assert.equal(state.board.value.lineId, 4);
  state.selectLine(4);
  state.selectLine(9);
  await settle();
  assert.equal(state.requests.length, 2);
});

test('a late response from the previous station cannot overwrite the current station', async t => {
  let resolveFirst;
  const state = await setup(t, url => url.includes('/theatre/')
    ? new Promise(resolve => { resolveFirst = resolve; }) : Promise.resolve(json(page(3, 'other'))));
  state.id.value = 'other';
  await settle();
  assert.equal(state.requests[0].init.signal.aborted, true);
  resolveFirst(json(page()));
  await settle();
  assert.equal(state.page.value.station.id, 'other');
  assert.equal(state.requests.length, 2);
});

test('unavailable timetable still displays the aggregate station data without extra requests', async t => {
  const state = await setup(t, async () => json({ ...page(), board: null, boardUnavailable: true }));
  assert.equal(state.page.value.station.id, 'theatre');
  assert.equal(state.boardFailed.value, true);
  assert.equal(state.failed.value, false);
  assert.equal(state.requests.length, 1);
});

test('resume refreshes only the board, and hidden pages do not poll', async t => {
  const state = await setup(t, async url => json(url.includes('/page') ? page() : board()));
  document.visibilityState = 'hidden';
  state.listeners.get('visibilitychange')();
  await settle();
  assert.equal(state.requests.length, 1);
  document.visibilityState = 'visible';
  state.listeners.get('visibilitychange')();
  await settle();
  assert.equal(state.requests.length, 2);
  assert.match(state.requests[1].url, /\/board\?lineId=3$/);
});

test('a new station waits a full refresh interval instead of inheriting the previous station timer', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const state = await setup(t, async url => json(url.includes('/page')
    ? page(3, url.includes('/other/') ? 'other' : 'theatre') : board(3, 'other')));
  t.mock.timers.tick(29_000);
  state.id.value = 'other';
  await settle();
  t.mock.timers.tick(1_000);
  await settle();
  assert.equal(state.requests.length, 2);
  t.mock.timers.tick(29_000);
  await settle();
  assert.equal(state.requests.length, 3);
  assert.equal(state.requests[2].url, '/api/timetable/stations/other/board?lineId=3');
});

const cachedNetwork = {
  lines: [{ id: 3, nameFa: 'خط ۳' }, { id: 4, nameFa: 'خط ۴' }],
  stations: ['theatre', 'other'].map(id => ({ id, nameFa: id === 'theatre' ? 'تئاتر شهر' : 'ایستگاه دیگر',
    lines: [{ lineId: 3, isOperational: true }], infrastructureStatus: 'operational' })),
  routes: [{ id: 'main', lineId: 3, stops: [] }],
};
const cachedTimetable = { requestedAtUtc: '2026-11-14T10:00:00Z', trips: [{
  id: 17, lineId: 3, routeId: 'main', directionStationId: 'other',
  position: { station: null, fromStation: 'theatre', toStation: 'other' },
}] };

test('slow loads immediately reuse station names and train positions, finishing animation after two seconds', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  let resolvePage;
  const state = await setup(t, () => new Promise(resolve => { resolvePage = resolve; }), undefined,
    { network: { data: cachedNetwork }, timetable: cachedTimetable });
  assert.equal(state.preview.value.station.nameFa, 'تئاتر شهر');
  assert.deepEqual(state.board.value.trips[0].position, cachedTimetable.trips[0].position);
  assert.equal(state.board.value.nextArrivals.length, 0);
  assert.equal(state.loading.value, true);
  assert.equal(state.entering.value, true);
  t.mock.timers.tick(1_999);
  assert.equal(state.entering.value, true);
  t.mock.timers.tick(1);
  assert.equal(state.entering.value, false);
  assert.equal(state.loading.value, true);
  assert.equal(state.requests.length, 1);
  resolvePage(json(page()));
  await settle();
  assert.equal(state.loading.value, false);
  assert.equal(state.page.value.station.id, 'theatre');
});

test('fast responses are applied immediately while the entrance animation completes independently', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const state = await setup(t);
  assert.equal(state.loading.value, false);
  assert.equal(state.page.value.station.id, 'theatre');
  assert.equal(state.entering.value, true);
  t.mock.timers.tick(2_000);
  assert.equal(state.entering.value, false);
  assert.equal(state.requests.length, 1);
});

test('failed updates retain the cached preview and mark the request failed', async t => {
  const state = await setup(t, async () => { throw new TypeError('offline'); }, undefined,
    { network: { data: cachedNetwork }, timetable: cachedTimetable });
  assert.equal(state.failed.value, true);
  assert.equal(state.preview.value.station.nameFa, 'تئاتر شهر');
  assert.equal(state.board.value.trips.length, 1);
  assert.equal(state.page.value, undefined);
  assert.equal(state.requests.length, 1);
});

test('borrowing a previous station board retains positions but discards station-specific arrival predictions', async t => {
  const state = await setup(t);
  const previous = { ...board(), nextArrivals: [{ tripId: 17 }], trips: cachedTimetable.trips };
  const preview = state.createPreview('other', cachedNetwork, previous);
  assert.equal(preview.station.id, 'other');
  assert.equal(preview.board.stationId, 'other');
  assert.deepEqual(preview.board.nextArrivals, []);
  assert.deepEqual(preview.board.trips, previous.trips);
  assert.equal(previous.nextArrivals.length, 1);
});

