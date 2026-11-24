import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/mapPreferences.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
  .replace("import { readonly, ref } from 'vue';", 'const ref = value => ({ value }); const readonly = value => value;')
  .replace("import { bridge } from './auth';", 'const bridge = async () => globalThis.mapPreferencesNative;');
const key = 'rail.map.preferences';
let instance = 0;
async function setup(storage = new Map(), native) {
  globalThis.localStorage = { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) };
  globalThis.mapPreferencesNative = native;
  const module = await import(`data:text/javascript;base64,${Buffer.from(compiled + `\n// instance ${instance++}`).toString('base64')}`);
  await module.initializeMapPreferences();
  return { ...module, ...module.useMapPreferences(), storage };
}

test('browser selections survive a new app session without depending on app version or network', async () => {
  const first = await setup();
  await first.setMapLineVisible(2, false);
  await first.setMapStationNames(false);
  await first.setMapStationNameBoxes(false);
  const reopened = await setup(first.storage);
  assert.deepEqual(reopened.preferences.value.hiddenLineIds, [2]);
  assert.equal(reopened.preferences.value.showStationNames, false);
  assert.equal(reopened.preferences.value.showStationNameBoxes, false);
  await reopened.setMapStationNames(true);
  assert.equal(reopened.preferences.value.showStationNameBoxes, false);
  await reopened.setMapLineVisible(2, true);
  assert.deepEqual(reopened.preferences.value.hiddenLineIds, []);
});

test('the master switch allows individual unfinished line selections and remembers them', async () => {
  const storage = new Map([[key, JSON.stringify({ hiddenLineIds: [2, 9], shownLineIds: [8], updatedAt: 10 })]]);
  const state = await setup(storage);
  const construction = [8, 9, 10, 11].map(id => ({ id, infrastructureStatus: 'under_construction' }));
  construction.push({ id: 12, infrastructureStatus: 'planned' });
  const visibleConstruction = state => construction.filter(line => state.isMapLineVisible(line, state.preferences.value));
  assert.deepEqual(visibleConstruction(state), []);
  assert.equal(state.isMapLineVisible({ id: 1 }, state.preferences.value), true);
  assert.equal(state.isMapLineVisible({ id: 2, infrastructureStatus: 'operational' }, state.preferences.value), false);
  await state.setMapUnbuiltStations(true);
  assert.deepEqual(visibleConstruction(state).map(line => line.id), [8]);
  await state.setMapLineVisible(8, false);
  await state.setMapLineVisible(9, true);
  await state.setMapLineVisible(11, true);

  const reopened = await setup(storage);
  assert.deepEqual(visibleConstruction(reopened).map(line => line.id), [9, 11]);
  assert.equal(reopened.isMapLineVisible({ id: 2 }, reopened.preferences.value), false);
  await reopened.setMapUnbuiltStations(false);
  assert.deepEqual(visibleConstruction(reopened), []);
  await reopened.setMapUnbuiltStations(true);
  assert.deepEqual(visibleConstruction(reopened).map(line => line.id), [9, 11]);

  const fresh = await setup();
  await fresh.setMapUnbuiltStations(true);
  assert.deepEqual(visibleConstruction(fresh), []);
});

test('native device preferences restore after the WebView local copy is absent', async () => {
  let saved;
  const native = { InvokeDotNet: async (method, args) => {
    if (method === 'ReadMapPreferences') return saved;
    if (method === 'WriteMapPreferences') saved = args[0];
  } };
  const first = await setup(new Map(), native);
  await first.setMapLineVisible(7, false);
  const afterUpdate = await setup(new Map(), native);
  assert.deepEqual(afterUpdate.preferences.value.hiddenLineIds, [7]);
  assert.deepEqual(JSON.parse(afterUpdate.storage.get(key)).hiddenLineIds, [7]);
});

test('the newest local copy wins over an older native write and repairs device storage', async () => {
  let device = JSON.stringify({ hiddenLineIds: [1], updatedAt: 10 });
  const storage = new Map([[key, JSON.stringify({ hiddenLineIds: [2], showStationNames: false, updatedAt: 20 })]]);
  const state = await setup(storage, { InvokeDotNet: async (method, args) => {
    if (method === 'ReadMapPreferences') return device;
    device = args[0];
  } });
  assert.deepEqual(state.preferences.value.hiddenLineIds, [2]);
  assert.equal(state.preferences.value.showStationNames, false);
  assert.deepEqual(JSON.parse(device).hiddenLineIds, [2]);
});

test('rapid changes serialize native writes so a slow save cannot overwrite the latest choice', async () => {
  let release, saved;
  const writes = [];
  const state = await setup(new Map(), { InvokeDotNet: async (method, args) => {
    if (method === 'ReadMapPreferences') return null;
    writes.push(args[0]);
    if (writes.length === 1) await new Promise(resolve => { release = resolve; });
    saved = args[0];
  } });
  const first = state.setMapLineVisible(1, false);
  const second = state.setMapStationNames(false);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(writes.length, 1);
  release();
  await Promise.all([first, second]);
  assert.deepEqual(JSON.parse(saved).hiddenLineIds, [1]);
  assert.equal(JSON.parse(saved).showStationNames, false);
});

test('invalid saved data is normalized and missing fields keep their defaults', async () => {
  const state = await setup(new Map([[key, JSON.stringify({ hiddenLineIds: [2, 2, -1, '3', 300],
    showStationNames: 'yes', showUnbuiltStations: 'yes' })]]));
  assert.deepEqual(state.preferences.value.hiddenLineIds, [2]);
  assert.equal(state.preferences.value.showStationNames, true);
  assert.equal(state.preferences.value.showStationNameBoxes, true);
  assert.equal(state.preferences.value.showUnbuiltStations, false);
  const corrupt = await setup(new Map([[key, '{']]));
  assert.deepEqual(corrupt.preferences.value.hiddenLineIds, []);
});

test('storage failure keeps the session usable and reports that preferences could not be saved', async () => {
  const state = await setup();
  globalThis.localStorage.setItem = () => { throw new Error('storage full'); };
  await state.setMapStationNames(false);
  assert.equal(state.preferences.value.showStationNames, false);
  assert.equal(state.storageFailed.value, true);
});
