import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';

const bundled = JSON.parse(await readFile(new URL('../../Rail.Statics/map-data/network.json', import.meta.url), 'utf8'));
const source = await readFile(new URL('../src/services/network.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
  .replace("import { readonly, ref, shallowReadonly, shallowRef } from 'vue';", 'const ref = value => ({ value }); const shallowRef = ref; const readonly = value => value; const shallowReadonly = readonly;')
  .replace("import { apiBase } from './api';", "const apiBase = '';")
  .replace("import bundledNetwork from '../../../Rail.Statics/map-data/network.json';", 'const bundledNetwork = globalThis.offlineNetworkFixture;');
let instance = 0;
const key = 'rail.network.v2:same-origin';
async function setup(storage = new Map()) {
  // Force a stale seed to prove first-run availability is independent of API freshness.
  globalThis.offlineNetworkFixture = { ...bundled, generatedAtUtc: '2000-01-01T00:00:00Z' };
  globalThis.localStorage = { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) };
  const module = await import(`data:text/javascript;base64,${Buffer.from(compiled + `\n// instance ${instance++}`).toString('base64')}`);
  return { ...module, ...module.useNetworkCache(), storage };
}

test('fresh install offline immediately displays bundled stations without a stale-cache modal', async () => {
  globalThis.fetch = async () => { throw new TypeError('No internet'); };
  const state = await setup();
  const pending = state.checkNetworkCache();
  assert.equal(state.snapshot.value.data.stations.length, 151);
  assert.equal(state.snapshot.value.bundled, true);
  await pending;
  assert.equal(state.failed.value, false);
  assert.equal(state.staleWarning.value, false);
});

test('an online snapshot replaces the seed and remains available on an offline restart', async () => {
  const downloaded = { ...bundled, generatedAtUtc: new Date().toISOString() };
  globalThis.fetch = async () => ({ ok: true, json: async () => downloaded });
  const state = await setup();
  await state.checkNetworkCache();
  assert.equal(state.snapshot.value.bundled, undefined);
  assert.ok(state.storage.has(key));
  globalThis.fetch = async () => { throw new TypeError('No internet'); };
  const restarted = await setup(state.storage);
  await restarted.checkNetworkCache();
  assert.equal(restarted.snapshot.value.data.generatedAtUtc, downloaded.generatedAtUtc);
  assert.equal(restarted.snapshot.value.persisted, true);
});

test('expired previously downloaded data retains the existing warning on failed refresh', async () => {
  globalThis.fetch = async () => { throw new TypeError('No internet'); };
  const state = await setup(new Map([[key, JSON.stringify({ data: bundled, savedAtUtc: '2000-01-01T00:00:00Z' })]]));
  await state.checkNetworkCache();
  assert.equal(state.snapshot.value.persisted, true);
  assert.equal(state.failed.value, false);
  assert.equal(state.staleWarning.value, true);
});
