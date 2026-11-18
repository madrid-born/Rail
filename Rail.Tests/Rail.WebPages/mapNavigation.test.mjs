import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/mapNavigation.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source.replace(/^import .*;\r?$/gm, ''), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText.replaceAll('export function', 'function');
function setup() {
  const selection = { from: [51.4, 35.7], to: [51.5, 35.8], query: { accessMode: 'car' } };
  const mapTripSelection = { value: selection };
  const service = new Function('mapTripSelection', compiled + '\nreturn { installMapPinNavigation, restoreTripPinsOnBack };')(mapTripSelection);
  const state = { position: 2 }; let before, after;
  service.installMapPinNavigation({ options: { history: { state } }, beforeEach: fn => { before = fn; }, afterEach: fn => { after = fn; } });
  const navigate = (from, to, position, query = {}) => { state.position = position; before({ name: to, query }, { name: from }); after(); };
  return { service, mapTripSelection, selection, navigate };
}
test('back from a trip restores pins, then a normal later map visit clears them', () => {
  const s = setup(); s.navigate('new-trip', 'map', 1);
  assert.equal(s.mapTripSelection.value, s.selection);
  s.navigate('map', 'stations', 2); s.navigate('stations', 'map', 3);
  assert.deepEqual(s.mapTripSelection.value, { query: {} });
});
test('opening Map directly from the trip tab does not restore the old pins', () => {
  const s = setup(); s.navigate('new-trip', 'map', 2);
  assert.deepEqual(s.mapTripSelection.value, { query: {} });
});
test('explicit back fallback restores pins once and endpoint editing preserves selection', () => {
  const s = setup(); s.service.restoreTripPinsOnBack(); s.navigate('new-trip', 'map', 2);
  assert.equal(s.mapTripSelection.value, s.selection);
  s.navigate('new-trip', 'map', 3); assert.deepEqual(s.mapTripSelection.value, { query: {} });
  s.mapTripSelection.value = s.selection; s.navigate('new-trip', 'map', 4, { pick: 'from' });
  assert.equal(s.mapTripSelection.value, s.selection);
});
