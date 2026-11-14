import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../src/services/journey.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { parseMapPoint, nearestStation, journeyGraph } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
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
