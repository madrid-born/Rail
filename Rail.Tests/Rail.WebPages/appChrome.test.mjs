import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/appChrome.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
  .replace("import { hasPreviousAppPage } from './pageViewState';", "const hasPreviousAppPage = back => typeof back === 'string' && back.startsWith('/') && !back.startsWith('//');");
const { installAppChrome } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
function setup(name = 'map', back = null, dialog = null) {
  globalThis.window = new EventTarget();
  globalThis.document = new EventTarget();
  document.querySelector = () => dialog;
  const calls = [];
  const router = { currentRoute: { value: { name } }, options: { history: { state: { back } } },
    back: () => calls.push('back'), replace: path => calls.push(path) };
  return { calls, dispose: installAppChrome(router) };
}
test('browser menus and drag ghosts are suppressed and listeners are cleaned up', () => {
  const s = setup();
  assert.equal(document.dispatchEvent(new Event('contextmenu', { cancelable: true })), false);
  assert.equal(document.dispatchEvent(new Event('dragstart', { cancelable: true })), false);
  s.dispose();
  assert.equal(document.dispatchEvent(new Event('contextmenu', { cancelable: true })), true);
  assert.equal(window.RailNative, undefined);
});
test('details navigate back before requesting an exit; deep links return to the map', () => {
  const s = setup('specific-station', '/map');
  assert.equal(window.RailNative.back(), false); assert.deepEqual(s.calls, ['back']); s.dispose();
  const deep = setup('specific-station', 'https://outside.example');
  assert.equal(window.RailNative.back(), false); assert.deepEqual(deep.calls, ['/map']); deep.dispose();
});
test('dialogs and map selection consume back; only an unhandled map back requests native exit', () => {
  const dialog = new EventTarget(); let closed = false; dialog.close = () => { closed = true; };
  const modal = setup('map', null, dialog);
  assert.equal(window.RailNative.back(), false); assert.equal(closed, true); modal.dispose();
  const map = setup();
  const closeSelection = event => event.preventDefault();
  window.addEventListener('rail:back', closeSelection);
  assert.equal(window.RailNative.back(), false);
  window.removeEventListener('rail:back', closeSelection);
  assert.equal(window.RailNative.back(), true);
  map.dispose();
});
