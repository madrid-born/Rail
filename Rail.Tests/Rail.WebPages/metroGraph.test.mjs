import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/metroGraph.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { buildMetroGraph } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
test('curved paths are route-specific and reverse without mutating their points', () => {
  const coordinates = [[51.1,35.7],[51.15,35.72],[51.2,35.7]];
  const data = network([station('a',51.1),station('b',51.2)],
    [{ id: 'main', lineId: 1, stops: [stop('b',1),stop('a',2)] }]);
  data.segments = [{ id: 1, routeId: 'main', fromStationId: 'a', toStationId: 'b', coordinates }];
  assert.deepEqual(buildMetroGraph(data).features.find(f => f.geometry.type === 'LineString').geometry.coordinates,
    [...coordinates].reverse());
  assert.deepEqual(coordinates[0], [51.1,35.7]);
  assert.equal(edges(buildMetroGraph(data))[0].properties.hasGeometry, true);
  data.segments[0].routeId = 'other';
  assert.equal(buildMetroGraph(data).features.find(f => f.geometry.type === 'LineString').geometry.coordinates.length, 2);
  assert.equal(edges(buildMetroGraph(data))[0].properties.hasGeometry, false);
});
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

test('only fully under-construction stations have dashed label borders', () => {
  const construction = station('construction');
  construction.infrastructureStatus = 'under_construction';
  construction.lines = [{ lineId: 1, isOperational: false }];
  const partial = station('partial');
  partial.infrastructureStatus = 'operational';
  partial.lines.push({ lineId: 2, isOperational: false });
  const closed = station('closed');
  closed.infrastructureStatus = 'temporarily_closed';
  closed.lines[0].isOperational = false;
  const graph = buildMetroGraph(network([construction,partial,closed], []));
  assert.equal(graph.features[0].properties.labelDashed, true);
  assert.equal(graph.features[0].properties.labelImage, 'metro-label-#123456-dashed');
  assert.equal(graph.features[1].properties.labelDashed, false);
  assert.deepEqual(graph.features[1].properties.labelColors, ['#123456']);
  assert.equal(graph.features[2].properties.labelDashed, false);
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

test('unfinished extensions and track-only construction respect the master switch', () => {
  const shared = station('shared', 51.2);
  shared.lines.push({ lineId: 8, isOperational: false });
  const construction = { ...station('construction', 51.3), infrastructureStatus: 'under_construction',
    lines: [{ lineId: 1, isOperational: false }] };
  const planned = { ...station('planned', 51.4), infrastructureStatus: 'planned',
    lines: [{ lineId: 8, isOperational: false }] };
  const data = network([station('a', 51.1), shared, construction, planned, station('b', 51.5), station('c', 51.6)], [
    { id: 'main', lineId: 1, stops: [stop('a', 1), stop('shared', 2), stop('construction', 3), stop('b', 4), stop('c', 5)] },
    { id: 'expansion', lineId: 8, stops: [stop('shared', 1, 'under_construction'), stop('planned', 2, 'planned')] },
  ]);
  data.lines.push({ id: 8, infrastructureStatus: 'under_construction', color: '#654321' });
  data.segments = [{ id: 1, routeId: 'main', fromStationId: 'b', toStationId: 'c', coordinates: [], status: 'under_construction' }];

  const hidden = buildMetroGraph(data, [8], false);
  assert.deepEqual(hidden.features.filter(f => f.geometry.type === 'Point').map(f => f.properties.stationId),
    ['a', 'shared', 'b', 'c']);
  assert.deepEqual(edges(hidden).map(f => f.geometry.coordinates), [[[51.1, 35.7], [51.2, 35.7]]]);

  const shown = buildMetroGraph(data, [], true);
  assert.deepEqual(shown.features.filter(f => f.geometry.type === 'Point' && f.properties.unbuilt)
    .map(f => f.properties.stationId), ['construction', 'planned']);
  assert.equal(shown.features.find(f => f.properties.stationId === 'shared').properties.unbuilt, false);
  assert.deepEqual(edges(shown).map(f => [f.properties.lineId, f.properties.unbuilt]),
    [[1, false], [1, true], [1, true], [1, true], [8, true]]);
});

test('the Parand branch retains built tracks through واوان while hiding its station marker', () => {
  const exhibition = station('namayeshgah-e-shahr-e-aftab', 51.2);
  const vavan = { ...station('vavan', 51.25), infrastructureStatus: 'under_construction' };
  const airport = station('emam-khomeini-airport', 51.3);
  const routeId = 'line-1-parand';
  const data = network([exhibition, vavan, airport], [{ id: routeId, lineId: 1,
    stops: [stop(exhibition.id, 1), stop(vavan.id, 2), stop(airport.id, 3)] }]);
  const beforeVavan = [[51.2, 35.7], [51.22, 35.71], [51.25, 35.7]];
  const afterVavan = [[51.25, 35.7], [51.28, 35.72], [51.3, 35.7]];
  data.segments = [
    { id: 1, routeId, fromStationId: exhibition.id, toStationId: vavan.id, coordinates: beforeVavan, status: 'operational' },
    { id: 2, routeId, fromStationId: vavan.id, toStationId: airport.id, coordinates: afterVavan, status: 'operational' },
  ];

  const graph = buildMetroGraph(data, [], false);
  assert.deepEqual(graph.features.filter(f => f.geometry.type === 'Point').map(f => f.properties.stationId),
    [exhibition.id, airport.id]);
  assert.deepEqual(edges(graph).map(f => f.geometry.coordinates), [beforeVavan, afterVavan]);
  assert.ok(edges(graph).every(f => f.properties.hasGeometry && !f.properties.unbuilt));

  delete data.segments[0].status; // Legacy snapshots still have the stored geometry.
  data.segments[1].status = 'under_construction';
  assert.deepEqual(edges(buildMetroGraph(data, [], false)).map(f => f.geometry.coordinates), [beforeVavan]);
});

test('شهدای هفده شهریور serves Line 7 while Line 6 track stays visible without a stop', () => {
  const stationId = 'shohada-ye-hefdah-e-shahrivar';
  const interchange = { ...station(stationId, 51.45), infrastructureStatus: 'operational',
    lines: [{ lineId: 6, isOperational: false }, { lineId: 7, isOperational: true }] };
  const neighbor = { ...station('meydan-e-khorasan', 51.46), lines: [{ lineId: 6, isOperational: true }] };
  const data = {
    lines: [{ id: 6, color: '#FD5C9C' }, { id: 7, color: '#78308A' }],
    stations: [interchange, neighbor],
    routes: [{ id: 'line-6-main', lineId: 6, stops: [stop(neighbor.id, 21), stop(stationId, 22, 'under_construction')] }],
    segments: [{ id: 1, routeId: 'line-6-main', fromStationId: neighbor.id, toStationId: stationId,
      coordinates: [[51.46, 35.7], [51.455, 35.71], [51.45, 35.7]], status: 'operational' }],
  };

  const bothLines = buildMetroGraph(data, [], false);
  const marker = bothLines.features.find(f => f.properties.stationId === stationId);
  assert.equal(marker.properties.interchange, false);
  assert.deepEqual(marker.properties.labelColors, ['#78308A']);

  const line6Only = buildMetroGraph(data, [7], false);
  assert.equal(line6Only.features.some(f => f.properties.stationId === stationId), false);
  assert.deepEqual(edges(line6Only).map(f => f.geometry.coordinates), [data.segments[0].coordinates]);
  assert.equal(buildMetroGraph(data, [7], true).features.some(f => f.properties.stationId === stationId), true);
});
