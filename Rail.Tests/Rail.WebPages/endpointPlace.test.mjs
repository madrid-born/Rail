import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { computed, effectScope, nextTick, ref, watch } from 'vue';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/endpointPlace.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source.replace(/^import .*;\r?$/gm, ''), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText.replace('export function', 'function');
const flush = async () => { await nextTick(); await new Promise(resolve => setImmediate(resolve)); };
function setup() {
  const calls = [];
  const lookup = (point, signal) => new Promise((resolve, reject) => calls.push({ point, signal,
    respond: (title, ok = true) => ok ? resolve(title) : reject(new Error('Unavailable archive')) }));
  const create = new Function('computed', 'ref', 'watch', 'findLocalPlace', compiled + '\nreturn useEndpointPlace;');
  return { calls, usePlace: create(computed, ref, watch, lookup) };
}
function mount(s, point, fallback) {
  const scope = effectScope();
  const title = scope.run(() => s.usePlace(computed(() => point.value), computed(() => fallback.value)));
  return { title, dispose: () => scope.stop() };
}

test('nearby metro title appears immediately and is replaced by the local map label', async () => {
  const s = setup(); const point = ref([51.4, 35.7]); const fallback = ref('Near Metro');
  const view = mount(s, point, fallback);
  assert.equal(view.title.value, 'Near Metro');
  assert.deepEqual(s.calls[0].point, [51.4, 35.7]);
  fallback.value = 'Nearby station loaded';
  assert.equal(view.title.value, 'Nearby station loaded');
  s.calls[0].respond('Selected street'); await flush();
  assert.equal(view.title.value, 'Selected street');
  view.dispose();
});

test('a late response for the old pin cannot overwrite the new endpoint', async () => {
  const s = setup(); const point = ref([51.4, 35.7]); const fallback = ref('Near Metro');
  const view = mount(s, point, fallback);
  point.value = [51.5, 35.8]; await flush();
  assert.equal(s.calls[0].signal.aborted, true);
  s.calls[1].respond('New place'); await flush();
  s.calls[0].respond('Old place'); await flush();
  assert.equal(view.title.value, 'New place');
  view.dispose();
});

test('failed lookups preserve fallback and completed titles are reused without another request', async () => {
  const s = setup(); const point = ref([51.4, 35.7]); const fallback = ref('Near Metro');
  const failed = mount(s, point, fallback);
  s.calls[0].respond('', false); await flush();
  assert.equal(failed.title.value, 'Near Metro'); failed.dispose();
  const retry = mount(s, point, fallback);
  s.calls[1].respond('Park'); await flush(); retry.dispose();
  const cached = mount(s, point, fallback);
  assert.equal(cached.title.value, 'Park');
  assert.equal(s.calls.length, 2); cached.dispose();
});

test('leaving the page cancels its pending local lookup', () => {
  const s = setup(); const view = mount(s, ref([51.4, 35.7]), ref('Near Metro'));
  view.dispose();
  assert.equal(s.calls[0].signal.aborted, true);
});
