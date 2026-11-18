import assert from 'node:assert/strict';
import { open, readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { test } from 'node:test';
import { PMTiles } from 'pmtiles';
import ts from 'typescript';
const requireWeb = createRequire(new URL('../../Rail.WebPages/package.json', import.meta.url));
const { VectorTile } = await import(pathToFileURL(requireWeb.resolve('@mapbox/vector-tile')));
const { PbfReader } = await import(pathToFileURL(requireWeb.resolve('pbf')));
const source = await readFile(new URL('../../Rail.WebPages/src/services/localPlace.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source.replace(/^import .*;\r?$/gm, ''), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText.replaceAll('export function', 'function').replaceAll('export const', 'const');
const { localPlaceTitle, createLocalPlaceLookup } = new Function('VectorTile', 'PbfReader', 'getMapArchive', compiled + '\nreturn { localPlaceTitle, createLocalPlaceLookup };')(VectorTile, PbfReader, () => { throw new Error('Unexpected archive access'); });
const feature = (layer, name, type, coordinates, properties = {}) => ({ layer,
  feature: { type: 'Feature', properties: { name, ...properties }, geometry: { type, coordinates } } });
const point = [51.4, 35.7];
const road = feature('roads', 'خیابان', 'LineString', [[51.399, 35.7001], [51.401, 35.7001]], { kind: 'major_road', kind_detail: 'secondary' });

test('titles use Persian main street names and ignore businesses even at the pin', () => {
  assert.equal(localPlaceTitle(point, [road, feature('pois', 'Far cafe', 'Point', [51.4009, 35.7])]), 'خیابان');
  assert.equal(localPlaceTitle(point, [{ ...road, feature: { ...road.feature, properties: { ...road.feature.properties, name: 'Street', 'name:fa': 'نام فارسی' } } }]), 'نام فارسی');
  assert.equal(localPlaceTitle(point, [road, feature('pois', 'پارک', 'Point', [51.4, 35.7])]), 'خیابان');
});

test('main street comes first, followed by the nearest smaller street, excluding highways and ramps', () => {
  const closest = [[51.399, 35.7], [51.401, 35.7]];
  const alternatives = [
    feature('roads', 'Alley', 'LineString', closest, { kind_detail: 'residential' }),
    feature('roads', 'Highway', 'LineString', closest, { kind_detail: 'trunk' }),
    feature('roads', 'Ramp', 'LineString', closest, { kind_detail: 'secondary', is_link: true }),
    feature('pois', 'Pharmacy', 'Point', point),
  ];
  assert.equal(localPlaceTitle(point, [road, ...alternatives]), 'خیابان، Alley');
  assert.equal(localPlaceTitle(point, alternatives), 'Alley');
});

test('the closest main street wins and distant streets or non-road features stay unavailable', () => {
  const farther = feature('roads', 'Farther main street', 'MultiLineString', [[[51.399, 35.7005], [51.401, 35.7005]]], { kind_detail: 'tertiary' });
  assert.equal(localPlaceTitle(point, [farther, road]), 'خیابان');
  assert.equal(localPlaceTitle(point, []), undefined);
  assert.equal(localPlaceTitle(point, [feature('roads', 'Far road', 'LineString', [[51.45, 35.7], [51.46, 35.7]], { kind_detail: 'primary' })]), undefined);
  assert.equal(localPlaceTitle(point, [feature('roads', 'Metro rail', 'LineString', [point, [51.401, 35.7]], { kind: 'rail' })]), undefined);
  assert.equal(localPlaceTitle(point, [feature('places', 'جلفا', 'Point', point, { kind: 'neighbourhood' })]), undefined);
});

test('street titles omit duplicates and distant side streets without leaving a dangling comma', () => {
  const sameName = feature('roads', 'خیابان', 'LineString', [[51.399, 35.7], [51.401, 35.7]], { kind_detail: 'residential' });
  const nearby = feature('roads', 'کوچه نزدیک', 'LineString', [[51.399, 35.7002], [51.401, 35.7002]], { kind_detail: 'living_street' });
  const farther = feature('roads', 'کوچه دورتر', 'LineString', [[51.399, 35.7004], [51.401, 35.7004]], { kind_detail: 'residential' });
  const tooFar = feature('roads', 'Far alley', 'LineString', [[51.399, 35.705], [51.401, 35.705]], { kind_detail: 'residential' });
  assert.equal(localPlaceTitle(point, [road, sameName, farther, nearby]), 'خیابان، کوچه نزدیک');
  assert.equal(localPlaceTitle(point, [road, sameName, tooFar]), 'خیابان');
});

test('lookup reads neighboring tiles, caches them, and skips coordinates outside the archive', async () => {
  const calls = [];
  const archive = { getHeader: async () => ({ minLon: 50.5, maxLon: 52, minLat: 35.3, maxLat: 36.2, maxZoom: 15 }),
    getZxy: async (z, x, y) => { calls.push([z, x, y]); return undefined; } };
  const lookup = createLocalPlaceLookup(async () => archive);
  assert.equal(await lookup(point), undefined);
  assert.equal(calls.length, 9);
  assert.equal(new Set(calls.map(call => call.join('/'))).size, 9);
  await lookup(point);
  assert.equal(calls.length, 9);
  await lookup([0, 0]);
  assert.equal(calls.length, 9);
  await assert.rejects(lookup(point, AbortSignal.abort()), { name: 'AbortError' });
});

test('real bundled PMTiles produces a local title through the native byte reader without network', async () => {
  const source = await readFile(new URL('../../Rail.WebPages/src/services/nativeMapSource.ts', import.meta.url), 'utf8');
  const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
  const { NativeMapSource } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  const file = await open(new URL('../../Rail.Resource/map-data/tehran-region.pmtiles', import.meta.url), 'r');
  let transferred = 0;
  try {
    const native = new NativeMapSource('offline://tehran', { InvokeDotNet: async (method, [offset, length]) => {
      assert.equal(method, 'ReadMapBytes'); transferred += length;
      const bytes = Buffer.alloc(length); const { bytesRead } = await file.read(bytes, 0, length, offset);
      assert.equal(bytesRead, length); return bytes.toString('base64');
    } });
    const lookup = createLocalPlaceLookup(async () => new PMTiles(native));
    const title = await lookup([51.44574522209393, 35.74337763342368]);
    assert.equal(typeof title, 'string'); assert.ok(title.length > 0);
    assert.match(title, /[\u0600-\u06ff]/);
    const reportedPoint = await lookup([51.35697737471844, 35.775494591573846]);
    assert.equal(reportedPoint, 'بلوار فرحزادی، استاد مطهری');
    assert.ok(transferred < 3 * 1024 * 1024, `Read ${transferred} bytes`);
  } finally { await file.close(); }
});
