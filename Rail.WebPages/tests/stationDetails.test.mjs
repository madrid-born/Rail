import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';

const source = await readFile(new URL('../src/services/stationDetails.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
  .replace("import { apiBase } from './api';", "const apiBase = '';");
let instance = 0;
async function setup() {
  const storage = new Map();
  globalThis.localStorage = { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) };
  const module = await import(`data:text/javascript;base64,${Buffer.from(compiled + `\n// instance ${instance++}`).toString('base64')}`);
  return { ...module, storage };
}
const route = count => ({ stops: Array.from({ length: count }, (_, index) => ({ stationId: String(index), sequence: index + 1 })).reverse() });

test('middle station shows two neighbors on each side in route sequence', async () => {
  const { stationWindow } = await setup();
  const result = stationWindow(route(9), '4');
  assert.equal(result.start, 2);
  assert.deepEqual(result.stops.map(stop => stop.stationId), ['2', '3', '4', '5', '6']);
});

test('both ends and near-terminal stations fill the window from the other side', async () => {
  const { stationWindow } = await setup();
  for (const id of ['0', '1']) assert.deepEqual(stationWindow(route(9), id).stops.map(stop => stop.stationId), ['0', '1', '2', '3', '4']);
  for (const id of ['7', '8']) assert.deepEqual(stationWindow(route(9), id).stops.map(stop => stop.stationId), ['4', '5', '6', '7', '8']);
});

test('short routes show only real stops and absent stations have no neighborhood', async () => {
  const { stationWindow } = await setup();
  for (const count of [1, 2, 4]) assert.equal(stationWindow(route(count), '0').stops.length, count);
  assert.deepEqual(stationWindow(route(9), 'missing').stops, []);
  assert.deepEqual(stationWindow(route(0), '0').stops, []);
});

test('first visits use the lowest line and preferences remain station-specific', async () => {
  const { preferredStationLine, saveStationLine, storage } = await setup();
  assert.equal(preferredStationLine('theatre', [4, 3]), 3);
  saveStationLine('theatre', 4);
  assert.equal(preferredStationLine('theatre', [3, 4]), 4);
  assert.equal(preferredStationLine('other', [3, 4]), 3);
  assert.equal(storage.get('rail.station.line.v1:theatre'), '4');
});

test('restores persisted preferences and falls back if the line was removed', async () => {
  const { preferredStationLine, storage } = await setup();
  storage.set('rail.station.line.v1:theatre', '4');
  assert.equal(preferredStationLine('theatre', [4, 3]), 4);
  assert.equal(preferredStationLine('theatre', [7, 3]), 3);
  storage.set('rail.station.line.v1:theatre', 'invalid');
  assert.equal(preferredStationLine('theatre', [4, 3]), 3);
  assert.equal(preferredStationLine('theatre', []), undefined);
});

test('unavailable persistent storage still allows selection during the session', async () => {
  const { preferredStationLine, saveStationLine } = await setup();
  globalThis.localStorage = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };
  assert.equal(preferredStationLine('theatre', [4, 3]), 3);
  saveStationLine('theatre', 4);
  assert.equal(preferredStationLine('theatre', [3, 4]), 4);
});

test('detail requests encode station IDs and report HTTP failures', async () => {
  const { fetchStationDetails } = await setup();
  const controller = new AbortController();
  globalThis.fetch = async (url, init) => {
    assert.equal(url, '/api/stations/a%2Fb');
    assert.equal(init.signal, controller.signal);
    return new Response(JSON.stringify({ id: 'a/b' }));
  };
  assert.deepEqual(await fetchStationDetails('a/b', controller.signal), { id: 'a/b' });
  globalThis.fetch = async () => new Response(null, { status: 404 });
  await assert.rejects(fetchStationDetails('missing', controller.signal), /404/);
});
