import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { computed, effectScope, reactive, ref, watch } from 'vue';
import ts from 'typescript';

const source = await readFile(new URL('../../Rail.WebPages/src/pages/MapPage.vue', import.meta.url), 'utf8');
const script = source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)[1].replace(/^import .*;\r?$/gm, '');
const compiled = ts.transpileModule(script, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
function setup(saved, query = {}) {
  const scope = effectScope();
  const calls = [];
  const mapTripSelection = ref(saved);
  const route = reactive({ query });
  const dependencies = { computed, ref, watch, onMounted() {}, onBeforeUnmount() {},
    useRoute: () => route, useRouter: () => ({ push: target => calls.push(target), replace: target => calls.push(target) }),
    useNetworkCache: () => ({ snapshot: ref(), failed: ref(false), loading: ref(false) }),
    useMapPreferences: () => ({ preferences: ref({}), open: ref(false), show() {} }),
    mapTripSelection, parseMapPoint: value => typeof value === 'string' ? value.split(',').map(Number) : undefined };
  const create = new Function(...Object.keys(dependencies), compiled + '\nreturn { selectedPoint, selectedDestination, choosingDestination, selectedStationId, cancelSelection, startTrip, handleBack };');
  const page = scope.run(() => create(...Object.values(dependencies)));
  return { page, calls, saved: mapTripSelection, dispose: () => scope.stop() };
}
const from = [51.4457, 35.7433];
const to = [51.3563, 35.7764];
const query = { dateTime: '2026-11-18T18:00', timeMode: 'arriveBy', accessMode: 'car', exitMode: 'walk' };

test('returning to map restores both endpoints and routing preserves trip options', () => {
  const s = setup({ from, to, stationId: 'origin', query });
  assert.deepEqual(s.page.selectedPoint.value, from);
  assert.deepEqual(s.page.selectedDestination.value, to);
  s.page.startTrip();
  assert.deepEqual(s.calls, [{ name: 'new-trip', query: { ...query, from: from.join(','), to: to.join(',') } }]);
  s.dispose();
});

test('cancel clears destination first, preserves origin, and survives remounting', () => {
  const s = setup({ from, to, stationId: 'origin', query });
  s.page.cancelSelection();
  assert.equal(s.page.selectedDestination.value, undefined);
  assert.deepEqual(s.page.selectedPoint.value, from);
  assert.equal(s.page.selectedStationId.value, 'origin');
  const reopened = setup(s.saved.value);
  assert.deepEqual(reopened.page.selectedPoint.value, from);
  assert.equal(reopened.page.selectedDestination.value, undefined);
  s.page.startTrip();
  assert.equal(s.calls.length, 0);
  s.page.cancelSelection();
  assert.equal(s.saved.value.from, undefined);
  assert.equal(s.saved.value.stationId, undefined);
  s.dispose(); reopened.dispose();
});

test('cancel during destination picking retains origin and hardware back clears only destination', () => {
  const s = setup({ from, query });
  s.page.choosingDestination.value = true;
  s.page.cancelSelection();
  assert.equal(s.page.choosingDestination.value, false);
  assert.deepEqual(s.page.selectedPoint.value, from);
  s.page.selectedDestination.value = to;
  const event = new Event('rail:back', { cancelable: true });
  s.page.handleBack(event);
  assert.equal(event.defaultPrevented, true);
  assert.equal(s.page.selectedDestination.value, undefined);
  assert.deepEqual(s.page.selectedPoint.value, from);
  s.dispose();
});

test('endpoint editing cancellation preserves the original selection and returns to trip', () => {
  const s = setup({ from, to, query }, { ...query, from: from.join(','), to: to.join(','), pick: 'to' });
  s.page.selectedPoint.value = [51.5, 35.8];
  s.page.cancelSelection();
  assert.deepEqual(s.saved.value.to, to);
  assert.deepEqual(s.calls, [{ name: 'new-trip', query: { ...query, from: from.join(','), to: to.join(',') } }]);
  s.dispose();
});
