import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../src/services/basemap.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { localizeBasemap } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);

test('Persian names take priority while road-number shields, symbols and attribution remain intact', () => {
  const style = { version: 8, glyphs: 'https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf',
    sources: { world: { type: 'vector', attribution: 'OpenMapTiles / OpenStreetMap' } }, layers: [
      { id: 'place', type: 'symbol', layout: { 'text-field': ['coalesce', ['get', 'name_en'], ['get', 'name']], 'text-size': 12 } },
      { id: 'road', type: 'symbol', layout: { 'text-field': '{name}' } },
      { id: 'shield', type: 'symbol', layout: { 'text-field': ['get', 'ref'] } },
      { id: 'arrow', type: 'symbol', layout: { 'icon-image': 'one-way' } },
      { id: 'water', type: 'fill', paint: { 'fill-color': '#abcdef' } }] };
  const localized = localizeBasemap(style);
  assert.deepEqual(localized.layers[0].layout['text-field'], ['coalesce', ['get', 'name:fa'], ['get', 'name'], ['get', 'name:nonlatin'], ['get', 'name:latin']]);
  assert.deepEqual(localized.layers[1].layout['text-field'], localized.layers[0].layout['text-field']);
  assert.equal(localized.layers[0].layout['text-size'], 12);
  assert.deepEqual(localized.layers.slice(2), style.layers.slice(2));
  assert.equal(localized.sources, style.sources);
  assert.equal(localized.glyphs, style.glyphs);
  assert.equal(style.layers[0].layout['text-field'][1][1], 'name_en');
});
