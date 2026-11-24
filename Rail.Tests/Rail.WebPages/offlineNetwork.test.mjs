import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';

const downloadedFixture = {
  schemaVersion: 1, generatedAtUtc: '2000-01-01T00:00:00Z',
  lines: [{ id: 1, nameFa: 'خط ۱', nameEn: 'Line 1', color: '#E0001F', routes: [{ id: 'route-1', branchCode: null }] }],
  stations: [{ id: 'station-1', nameFa: 'ایستگاه', nameEn: 'Station', latitude: 35.7, longitude: 51.4,
    infrastructureStatus: 'operational', lines: [{ lineId: 1, isOperational: true }] }],
  routes: [{ id: 'route-1', lineId: 1, branchCode: null, stops: [
    { stationId: 'station-1', sequence: 1, status: 'operational', nameFa: 'ایستگاه', nameEn: 'Station' },
  ] }],
};
const source = await readFile(new URL('../../Rail.WebPages/src/services/network.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
  .replace("import { readonly, ref, shallowReadonly, shallowRef } from 'vue';", 'const ref = value => ({ value }); const shallowRef = ref; const readonly = value => value; const shallowReadonly = readonly;')
  .replace("import { apiBase } from './api';", "const apiBase = '';");
let instance = 0;
const key = 'rail.network.v11:same-origin';
test('ordered segment geometry survives caching and invalid downloaded points cannot replace it', async () => {
  const data = structuredClone(downloadedFixture);
  data.stations.push({ ...data.stations[0], id: 'station-2', longitude: 51.42 });
  data.segments = [{ id: 7, routeId: 'route-1', fromStationId: 'station-1', toStationId: 'station-2',
    coordinates: [[51.4,35.7],[51.41,35.71],[51.42,35.7]], status: 'under_construction' }];
  globalThis.fetch = async () => ({ ok: true, json: async () => data });
  const first = await setup();
  await first.checkNetworkCache();
  assert.deepEqual(first.snapshot.value.data.segments[0], data.segments[0]);
  const saved = JSON.stringify({ data, savedAtUtc: '2000-01-01T00:00:00Z' });
  const storage = new Map([[key, saved]]);
  const bad = structuredClone(data);
  bad.segments[0].coordinates[1] = [181,35.7];
  globalThis.fetch = async () => ({ ok: true, json: async () => bad });
  const second = await setup(storage);
  await second.checkNetworkCache();
  assert.equal(storage.get(key), saved);
  assert.deepEqual(second.snapshot.value.data.segments[0].coordinates, data.segments[0].coordinates);
});
async function setup(storage = new Map()) {
  globalThis.localStorage = { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) };
  const module = await import(`data:text/javascript;base64,${Buffer.from(compiled + `\n// instance ${instance++}`).toString('base64')}`);
  return { ...module, ...module.useNetworkCache(), storage };
}

test('fresh install offline reports unavailable data without inventing stations', async () => {
  globalThis.fetch = async () => { throw new TypeError('No internet'); };
  const state = await setup();
  const pending = state.checkNetworkCache();
  assert.equal(state.snapshot.value, undefined);
  await pending;
  assert.equal(state.failed.value, true);
  assert.equal(state.staleWarning.value, false);
});

test('a recent legacy snapshot refreshes immediately and detects construction lines from older APIs', async () => {
  const data = { ...structuredClone(downloadedFixture), segments: [] };
  const legacyKey = 'rail.network.v10:same-origin';
  const old = JSON.stringify({ data, savedAtUtc: new Date().toISOString() });
  const state = await setup(new Map([[legacyKey, old]]));
  globalThis.fetch = async () => { throw new TypeError('Offline'); };
  assert.equal(await state.checkNetworkCache(), false);
  assert.equal(state.snapshot.value.data.lines.length, 1);
  assert.equal(state.storage.get(legacyKey), old);

  data.lines.push({ id: 8, nameFa: 'خط ۸', nameEn: 'Line 8', color: '#c67a1a', routes: [{ id: 'route-8', branchCode: null }] });
  data.routes.push({ id: 'route-8', lineId: 8, branchCode: null, stops: [
    { ...data.routes[0].stops[0], status: 'under_construction' },
  ] });
  globalThis.fetch = async () => ({ ok: true, json: async () => data });
  assert.equal(await state.checkNetworkCache(), true);
  assert.equal(state.snapshot.value.data.lines[0].infrastructureStatus, undefined);
  assert.equal(state.snapshot.value.data.lines[1].infrastructureStatus, 'under_construction');
  assert.ok(state.storage.has(key));
});

test('resume adopts a newer snapshot saved by another tab without making an API request', async () => {
  const data = { ...downloadedFixture, segments: [] };
  const state = await setup(new Map([[key, JSON.stringify({ data, savedAtUtc: new Date(Date.now()-60000).toISOString() })]]));
  globalThis.fetch = async () => { throw new Error('Fresh cache should not fetch'); };
  await state.checkNetworkCache();
  const updated = { ...data, stations: data.stations.map(s => ({ ...s, nameEn: 'Newer saved station' })) };
  state.storage.set(key, JSON.stringify({ data: updated, savedAtUtc: new Date().toISOString() }));
  assert.equal(await state.checkNetworkCache(), true);
  assert.equal(state.snapshot.value.data.stations[0].nameEn, 'Newer saved station');
});

test('fresh cache skips automatic fetch, explicit refresh replaces paths, failed refresh preserves them', async () => {
  const cached = { ...downloadedFixture, segments: [] };
  const state = await setup(new Map([[key, JSON.stringify({ data: cached, savedAtUtc: new Date().toISOString() })]]));
  let calls = 0;
  const updated = { ...cached, generatedAtUtc: new Date().toISOString(), stations: cached.stations.map(s => ({ ...s, nameEn: 'Updated station' })) };
  globalThis.fetch = async () => { calls++; return { ok: true, json: async () => updated }; };
  await state.checkNetworkCache();
  assert.equal(calls, 0);
  assert.equal(await state.checkNetworkCache(true), true);
  assert.equal(calls, 1);
  assert.equal(state.snapshot.value.data.stations[0].nameEn, 'Updated station');
  const saved = state.storage.get(key);
  globalThis.fetch = async () => { throw new TypeError('Offline'); };
  assert.equal(await state.checkNetworkCache(true), false);
  assert.equal(state.storage.get(key), saved);
  assert.equal(state.snapshot.value.data.stations[0].nameEn, 'Updated station');
});

test('an online snapshot remains available on an offline restart', async () => {
  const downloaded = { ...downloadedFixture, generatedAtUtc: new Date().toISOString() };
  globalThis.fetch = async () => ({ ok: true, json: async () => downloaded });
  const state = await setup();
  await state.checkNetworkCache();
  assert.equal(state.snapshot.value.data.stations[0].id, 'station-1');
  assert.ok(state.storage.has(key));
  globalThis.fetch = async () => { throw new TypeError('No internet'); };
  const restarted = await setup(state.storage);
  await restarted.checkNetworkCache();
  assert.equal(restarted.snapshot.value.data.generatedAtUtc, downloaded.generatedAtUtc);
  assert.equal(restarted.snapshot.value.persisted, true);
});

test('expired previously downloaded data retains the existing warning on failed refresh', async () => {
  globalThis.fetch = async () => { throw new TypeError('No internet'); };
  const state = await setup(new Map([[key, JSON.stringify({ data: downloadedFixture, savedAtUtc: '2000-01-01T00:00:00Z' })]]));
  await state.checkNetworkCache();
  assert.equal(state.snapshot.value.persisted, true);
  assert.equal(state.failed.value, false);
  assert.equal(state.staleWarning.value, true);
});
