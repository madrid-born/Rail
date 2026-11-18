import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/tripCache.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
async function cache() { return import(`data:text/javascript;base64,${Buffer.from(compiled + '\n// ' + Math.random()).toString('base64')}`); }

test('returning to a trip restores its response and selected alternative without extending freshness', async () => {
  const c = await cache(); const response = { id: 'fast', options: [{ id: 'fast' }, { id: 'chosen' }] };
  c.cacheTrip('/trip?a', response, 'fast', 1000);
  c.rememberTripChoice('/trip?a', 'chosen');
  assert.equal(c.cachedTrip('/trip?a').response, response);
  assert.equal(c.cachedTrip('/trip?a').selectedChoiceId, 'chosen');
  assert.equal(c.isTripFresh('/trip?a', 30_999), true);
  assert.equal(c.isTripFresh('/trip?a', 31_000), false);
  assert.equal(c.cachedTrip('/trip?a').response, response); // Stale data remains immediately available.
});

test("different endpoint, time and mode queries cannot reuse each other's trip", async () => {
  const c = await cache(); c.cacheTrip('/trip?from=a&time=18&mode=walk', { id: 'walk' });
  assert.equal(c.cachedTrip('/trip?from=b&time=18&mode=walk'), undefined);
  assert.equal(c.cachedTrip('/trip?from=a&time=19&mode=walk'), undefined);
  assert.equal(c.cachedTrip('/trip?from=a&time=18&mode=car'), undefined);
});

test('cache keeps recently visited trips and replaces successful background results', async () => {
  const c = await cache();
  for (let i = 0; i < 8; i++) c.cacheTrip(String(i), { id: String(i) }, undefined, 1000);
  c.cachedTrip('0'); c.cacheTrip('8', { id: '8' }, undefined, 2000);
  assert.ok(c.cachedTrip('0')); assert.equal(c.cachedTrip('1'), undefined);
  c.cacheTrip('0', { id: 'updated' }, 'updated', 5000);
  assert.equal(c.cachedTrip('0').response.id, 'updated');
  assert.equal(c.isTripFresh('0', 5001), true);
});
