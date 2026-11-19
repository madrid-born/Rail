import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/journey.ts', import.meta.url), 'utf8');
const metroSource = await readFile(new URL('../../Rail.WebPages/src/services/metroGraph.ts', import.meta.url), 'utf8');
const metroCompiled = ts.transpileModule(metroSource, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const metroUrl = `data:text/javascript;base64,${Buffer.from(metroCompiled).toString('base64')}`;
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
  .replace("'./metroGraph'", JSON.stringify(metroUrl));
const { parseMapPoint, nearestStation, journeyGraph, journeyLabelStations, intermediateStations, addStreetPaths } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const station = (id, longitude, lines = [1]) => ({ id, nameFa: id, longitude, latitude: 35.7,
  infrastructureStatus: 'operational', lines: lines.map(lineId => ({ lineId, isOperational: true })) });
const route = (id, lineId, ids) => ({ id, lineId, stops: ids.map((stationId, sequence) => ({ stationId, sequence, status: 'operational' })) });
const network = { stations: [station('a', 51.1), station('b', 51.2), station('c', 51.3, [1, 2]), station('d', 51.4), station('x', 51.5, [2])],
  lines: [{ id: 1, color: '#123456' }, { id: 2, color: '#654321' }], routes: [route('main', 1, ['a', 'b', 'c', 'd']), route('branch', 2, ['c', 'x'])] };
test('map coordinates reject malformed, missing and out-of-range deep links', () => {
  assert.deepEqual(parseMapPoint('51.4,35.7'), [51.4, 35.7]);
  for (const value of [undefined, ['51,35'], '181,35', '51,91', ',', '51,', 'NaN,35']) assert.equal(parseMapPoint(value), undefined);
});
test('nearest station ignores inactive stations, unavailable lines and missing coordinates', () => {
  const data = structuredClone(network);
  data.stations[0].infrastructureStatus = 'under_construction';
  data.stations[1].lines[0].isOperational = false;
  data.stations.push(station('unrouted', 51.1));
  assert.equal(nearestStation(data, [51.1, 35.7]).id, 'c');
  assert.equal(nearestStation({ ...data, stations: [] }, [51.1, 35.7]), undefined);
});
test('route map contains only the journey segment and names endpoints and intersections', () => {
  const graph = journeyGraph(network, [{ routeId: 'main', lineId: 1, fromStationId: 'a', toStationId: 'd' }]);
  const points = graph.features.filter(feature => feature.geometry.type === 'Point');
  assert.deepEqual(points.map(feature => feature.properties.stationId), ['a', 'b', 'c', 'd']);
  assert.deepEqual(points.filter(feature => feature.properties.named).map(feature => feature.properties.stationId), ['a', 'c', 'd']);
});
test('reverse travel and transfers preserve direction and use a single interchange node', () => {
  const graph = journeyGraph(network, [{ routeId: 'main', lineId: 1, fromStationId: 'd', toStationId: 'c' },
    { routeId: 'branch', lineId: 2, fromStationId: 'c', toStationId: 'x' }]);
  assert.deepEqual(graph.features.filter(feature => feature.geometry.type === 'LineString').map(feature => feature.geometry.coordinates), [
    [[51.4, 35.7], [51.3, 35.7]], [[51.3, 35.7], [51.5, 35.7]]]);
  assert.equal(graph.features.filter(feature => feature.properties.stationId === 'c').length, 1);
});
test('missing route geometry does not draw a false shortcut', () => {
  const data = structuredClone(network);
  data.stations[1].longitude = NaN;
  const graph = journeyGraph(data, [{ routeId: 'main', lineId: 1, fromStationId: 'a', toStationId: 'c' }]);
  assert.equal(graph.features.filter(feature => feature.geometry.type === 'LineString').length, 0);
  assert.throws(() => journeyGraph(data, [{ routeId: 'missing' }]));
});

test('street paths include the geometry flag required by map layers for every travel mode', () => {
  const coordinates = [[51.4, 35.7], [51.41, 35.71]];
  const paths = ['car', 'motorcycle', 'walk'].map(mode => ({ mode, source: 'graphhopper', coordinates }));
  const graph = addStreetPaths({ type: 'FeatureCollection', features: [] }, [...paths,
    { mode: 'car', source: 'estimated', coordinates: [] }, null]);
  assert.equal(graph.features.length, 3);
  for (const feature of graph.features) {
    assert.equal(feature.properties.hasGeometry, true);
    assert.deepEqual(feature.geometry.coordinates, coordinates);
  }
  assert.deepEqual(graph.features.map(f => f.properties.walking), [false, false, true]);
  assert.deepEqual(graph.features.map(f => f.properties.color), ['#000000', '#000000', '#4285f4']);
});

test('trip maps label endpoints and actual transfers with the lines used at each station', () => {
  const legs = [{ routeId: 'main', lineId: 1, fromStationId: 'a', toStationId: 'c' },
    { routeId: 'branch', lineId: 2, fromStationId: 'c', toStationId: 'x' }];
  const graph = journeyGraph(network, legs, true);
  assert.deepEqual(graph.features.filter(f => f.geometry.type === 'Point' && f.properties.named)
    .map(f => f.properties.stationId), ['a', 'c', 'x']);
  assert.equal(graph.features.filter(f => f.geometry.type === 'Point').length, 4);
  const colors = id => graph.features.find(f => f.properties.stationId === id).properties.labelColors;
  assert.deepEqual(colors('a'), ['#123456']);
  assert.deepEqual(colors('c'), ['#123456', '#654321']);
  assert.deepEqual(colors('x'), ['#654321']);
});

test('boarding at or passing an interchange uses only the line ridden and does not imply a transfer', () => {
  const boarding = journeyGraph(network, [{ routeId: 'main', lineId: 1, fromStationId: 'c', toStationId: 'd' }], true);
  const start = boarding.features.find(f => f.properties.stationId === 'c');
  assert.equal(start.properties.named, true);
  assert.deepEqual(start.properties.labelColors, ['#123456']);
  const passing = journeyGraph(network, [{ routeId: 'main', lineId: 1, fromStationId: 'a', toStationId: 'd' }], true);
  assert.deepEqual(passing.features.filter(f => f.properties.named).map(f => f.properties.stationId), ['a', 'd']);
  assert.deepEqual(passing.features.find(f => f.properties.stationId === 'c').properties.labelColors, ['#123456']);
});

test('map label stations include endpoints and actual transfers in travel order', () => {
  const legs = [{ lineId: 1, fromStationId: 'a', toStationId: 'c' },
    { lineId: 2, fromStationId: 'c', toStationId: 'x' }];
  assert.deepEqual(journeyLabelStations(legs), ['a', 'c', 'x']);
  assert.deepEqual(journeyLabelStations([{ lineId: 1, fromStationId: 'c', toStationId: 'd' }]), ['c', 'd']);
  assert.deepEqual(journeyLabelStations([{ lineId: 1, fromStationId: 'a', toStationId: 'b' },
    { lineId: 1, fromStationId: 'b', toStationId: 'd' }]), ['a', 'd']);
  assert.deepEqual(journeyLabelStations([]), []);
});

test('intermediate stations exclude endpoints and preserve travel order in either direction', () => {
  const forward = { routeId: 'main', fromStationId: 'a', toStationId: 'd' };
  assert.deepEqual(intermediateStations(network, forward).map(s => s.id), ['b', 'c']);
  assert.deepEqual(intermediateStations(network, { ...forward, fromStationId: 'd', toStationId: 'a' }).map(s => s.id), ['c', 'b']);
  assert.deepEqual(intermediateStations(network, { ...forward, toStationId: 'b' }), []);
  assert.deepEqual(intermediateStations(undefined, forward), []);
  assert.deepEqual(intermediateStations(network, { ...forward, routeId: 'missing' }), []);
});
