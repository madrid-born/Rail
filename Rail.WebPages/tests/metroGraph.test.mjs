import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../src/services/metroGraph.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { buildMetroGraph } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const station = (id, longitude = 51.4, latitude = 35.7) => ({ id, nameFa: id, longitude, latitude, lines: [{ lineId: 1, isOperational: true }] });
const stop = (stationId, sequence, status = 'operational') => ({ stationId, sequence, status });
const network = (stations, routes) => ({ stations, routes, lines: [{ id: 1, color: '#123456' }] });
const edges = graph => graph.features.filter(f => f.geometry.type === 'LineString');

test('connects stops by sequence with longitude first and preserves input order', () => {
  const data = network([station('a', 51.1), station('b', 51.2), station('c', 51.3)],
    [{ id: 'main', lineId: 1, stops: [stop('c', 3), stop('a', 1), stop('b', 2)] }]);
  const graph = buildMetroGraph(data);
  assert.deepEqual(edges(graph).map(f => f.geometry.coordinates), [
    [[51.1, 35.7], [51.2, 35.7]], [[51.2, 35.7], [51.3, 35.7]]]);
  assert.equal(edges(graph)[0].properties.color, '#123456');
  assert.equal(data.routes[0].stops[0].stationId, 'c');
});

test('branches stay separate and interchanges have a single station node', () => {
  const shared = station('a');
  shared.lines.push({ lineId: 2, isOperational: true });
  const graph = buildMetroGraph(network([shared, station('b'), station('c')], [
    { id: 'main', lineId: 1, stops: [stop('a', 1), stop('b', 2)] },
    { id: 'branch', lineId: 1, stops: [stop('a', 1), stop('c', 2, 'under_construction')] }]));
  assert.equal(edges(graph).length, 2);
  assert.deepEqual(edges(graph).map(f => f.properties.routeId), ['main', 'branch']);
  assert.equal(edges(graph)[1].properties.operational, false);
  assert.equal(graph.features.filter(f => f.properties.stationId === 'a').length, 1);
  assert.equal(graph.features.find(f => f.properties.stationId === 'a').properties.interchange, true);
});

test('missing or invalid coordinates break connections without bridging the gap', () => {
  const data = network([station('a'), station('bad', 0, 0), station('c'), station('invalid', NaN)],
    [{ id: 'main', lineId: 1, stops: [stop('a', 1), stop('bad', 2), stop('c', 3), stop('missing', 4)] }]);
  const graph = buildMetroGraph(data);
  assert.equal(edges(graph).length, 0);
  assert.equal(graph.features.length, 2);
});

test('label borders use stable active line colors and retain a color for inactive stations', () => {
  const shared = station('shared');
  shared.lines = [{ lineId: 2, isOperational: true }, { lineId: 3, isOperational: false },
    { lineId: 1, isOperational: true }];
  const inactive = station('inactive');
  inactive.lines = [{ lineId: 3, isOperational: false }];
  const graph = buildMetroGraph({ stations: [shared, inactive], routes: [], lines: [
    { id: 1, color: '#E0001F' }, { id: 2, color: '#2F4389' }, { id: 3, color: '#67C5F5' }] });
  assert.deepEqual(graph.features[0].properties.labelColors, ['#E0001F', '#2F4389']);
  assert.equal(graph.features[0].properties.labelImage, 'metro-label-#E0001F-#2F4389');
  assert.deepEqual(graph.features[1].properties.labelColors, ['#67C5F5']);
});

test('line filtering hides tracks and exclusive stations without changing interchange colors', () => {
  const shared = station('shared');
  shared.lines.push({ lineId: 2, isOperational: true });
  const hiddenOnly = station('hidden');
  const visibleOnly = station('visible');
  visibleOnly.lines = [{ lineId: 2, isOperational: true }];
  const data = { stations: [shared, hiddenOnly, visibleOnly],
    lines: [{ id: 1, color: '#E0001F' }, { id: 2, color: '#2F4389' }], routes: [
      { id: 'one', lineId: 1, stops: [stop('shared', 1), stop('hidden', 2)] },
      { id: 'two', lineId: 2, stops: [stop('shared', 1), stop('visible', 2)] }] };
  const graph = buildMetroGraph(data, [1]);
  assert.deepEqual(graph.features.filter(f => f.geometry.type === 'Point').map(f => f.properties.stationId), ['shared', 'visible']);
  assert.equal(edges(graph).length, 1);
  assert.equal(edges(graph)[0].properties.lineId, 2);
  const originalShared = buildMetroGraph(data).features.find(feature => feature.properties.stationId === 'shared');
  assert.deepEqual(graph.features[0].properties.labelColors, ['#E0001F', '#2F4389']);
  assert.equal(graph.features[0].properties.labelImage, originalShared.properties.labelImage);
  assert.equal(graph.features[0].properties.interchange, true);
  assert.deepEqual(buildMetroGraph(data, [1, 2]).features, []);
});
