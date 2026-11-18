import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/mapStationSelection.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const { installMapStationSelection } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
function setup(t) {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const events = new Map(); const selected = [];
  const map = { on: (event, fn) => events.set(event, fn), off: event => events.delete(event) };
  return { selection: installMapStationSelection(map, id => selected.push(id)), selected,
    emit: event => events.get(event)?.(), events };
}
test('a single click opens a station after the double-tap window', t => {
  const s = setup(t); s.selection.select('station'); t.mock.timers.tick(499);
  assert.deepEqual(s.selected, []); t.mock.timers.tick(1);
  assert.deepEqual(s.selected, ['station']); s.selection.dispose();
});
test('double click and double tap prioritize zoom without opening a station', t => {
  const s = setup(t);
  s.selection.select('station'); t.mock.timers.tick(200); s.emit('mousedown');
  s.selection.select('station'); s.emit('dblclick'); s.emit('zoomstart');
  t.mock.timers.tick(1000); assert.deepEqual(s.selected, []);
  s.selection.select('station'); t.mock.timers.tick(450); s.emit('touchstart'); s.emit('zoomstart');
  t.mock.timers.tick(1000); assert.deepEqual(s.selected, []); s.selection.dispose();
});
test('panning, rotation and disposal cancel pending station navigation', t => {
  const s = setup(t);
  for (const event of ['dragstart', 'rotatestart']) {
    s.selection.select('station'); s.emit(event); t.mock.timers.tick(1000);
  }
  s.selection.select('station'); s.selection.dispose(); t.mock.timers.tick(1000);
  assert.deepEqual(s.selected, []); assert.equal(s.events.size, 0);
});
