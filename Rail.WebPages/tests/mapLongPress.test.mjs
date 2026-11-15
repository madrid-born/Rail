import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../src/services/mapLongPress.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { installMapLongPress } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
function setup(t) {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const canvas = new EventTarget();
  canvas.getBoundingClientRect = () => ({ left: 10, top: 20 });
  const points = [];
  const controller = installMapLongPress(canvas, point => points.push(point));
  const pointer = (type, properties = {}) => canvas.dispatchEvent(Object.assign(new Event(type, { cancelable: true }),
    { pointerId: 1, isPrimary: true, button: 0, clientX: 100, clientY: 200, ...properties }));
  const click = () => canvas.dispatchEvent(new Event('click', { cancelable: true }));
  return { points, controller, pointer, click };
}
test('a normal tap remains a click and does not create a pin', t => {
  const s = setup(t);
  s.pointer('pointerdown'); t.mock.timers.tick(100); s.pointer('pointerup');
  t.mock.timers.tick(1000);
  assert.deepEqual(s.points, []);
  assert.equal(s.click(), true);
  s.controller.dispose();
});
test('holding drops one pin and suppresses the release click, while the next tap works', t => {
  const s = setup(t);
  s.pointer('pointerdown'); t.mock.timers.tick(549);
  assert.deepEqual(s.points, []);
  t.mock.timers.tick(1);
  assert.deepEqual(s.points, [[90, 180]]);
  t.mock.timers.tick(3000); // Keeping the pointer held never rearms station navigation.
  assert.equal(s.controller.suppressClick(), true);
  s.pointer('pointerup');
  assert.equal(s.click(), false);
  s.pointer('pointerdown'); s.pointer('pointerup');
  assert.equal(s.click(), true);
  assert.equal(s.points.length, 1);
  s.controller.dispose();
});
test('panning, multitouch, right clicks and pointer cancellation never create pins', t => {
  const s = setup(t);
  for (const cancel of [() => s.pointer('pointermove', { clientX: 115 }),
    () => s.pointer('pointerdown', { pointerId: 2, isPrimary: false }),
    () => s.pointer('pointercancel'), () => s.pointer('pointerleave'), () => s.controller.cancel()]) {
    s.pointer('pointerdown'); cancel(); t.mock.timers.tick(600);
  }
  s.pointer('pointerdown', { button: 2 }); t.mock.timers.tick(600);
  assert.deepEqual(s.points, []);
  s.controller.dispose();
});
test('small finger movement is tolerated and disposal cancels pending holds', t => {
  const s = setup(t);
  s.pointer('pointerdown'); s.pointer('pointermove', { clientX: 104, clientY: 203 });
  t.mock.timers.tick(550); assert.equal(s.points.length, 1);
  s.pointer('pointerup'); s.pointer('pointerdown'); s.controller.dispose();
  t.mock.timers.tick(1000); assert.equal(s.points.length, 1);
  assert.equal(s.click(), true);
});
