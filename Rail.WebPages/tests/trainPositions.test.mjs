import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';

const source = await readFile(new URL('../src/services/trainPositions.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext } }).outputText;
const { locateTrains, stationOccupancy } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const route = { id: 'main', lineId: 1, branchCode: null,
  stops: ['d', 'b', 'a', 'c'].map(id => ({ stationId: id, sequence: 'abcd'.indexOf(id) + 1 })) };
const trip = (position, directionStationId = 'd', extra = {}) => ({
  id: 1, routeId: 'main', lineId: 1, directionStationId,
  position: { station: null, fromStation: null, toStation: null, ...position }, ...extra,
});

test('at-station trains use the target terminal to choose opposite tracks', () => {
  const markers = locateTrains(route, [trip({ station: 'b' }), trip({ station: 'b' }, 'a', { id: 2 })]);
  assert.deepEqual(markers.map(m => [m.direction, m.fromIndex, m.toIndex]),
    [['forward', 1, 1], ['backward', 1, 1]]);
});

test('moving trains follow segment order, including reverse travel', () => {
  const markers = locateTrains(route, [trip({ fromStation: 'a', toStation: 'b' }),
    trip({ fromStation: 'c', toStation: 'b' }, 'a', { id: 2 })]);
  assert.deepEqual(markers.map(m => [m.direction, m.fromIndex, m.toIndex]),
    [['forward', 0, 1], ['backward', 2, 1]]);
});

test('express segments span their actual endpoints, not only adjacent stations', () => {
  const [marker] = locateTrains(route, [trip({ fromStation: 'a', toStation: 'd' })]);
  assert.equal(marker.fromIndex, 0);
  assert.equal(marker.toIndex, 3);
});

test('branches and other lines never leak onto this route', () => {
  assert.deepEqual(locateTrains(route, [trip({ station: 'b' }, 'd', { routeId: 'branch' }),
    trip({ station: 'b' }, 'd', { lineId: 2 })]), []);
});

test('future trips, unknown stations, and unresolvable directions draw no train', () => {
  assert.deepEqual(locateTrains(route, [trip({}), trip({ station: 'missing' }),
    trip({ station: 'b' }, 'missing'), trip({ fromStation: 'a', toStation: 'missing' }),
    trip({ fromStation: 'a', toStation: 'a' })]), []);
});

test('terminal station positions retain their direction', () => {
  const markers = locateTrains(route, [trip({ station: 'a' }, 'a'), trip({ station: 'd' }, 'd', { id: 2 })]);
  assert.deepEqual(markers.map(m => m.direction), ['backward', 'forward']);
});

test('multiple occurrences remain distinct even when trip IDs overlap', () => {
  const markers = locateTrains(route, [trip({ station: 'b' }), trip({ station: 'c' })]);
  assert.equal(new Set(markers.map(m => m.key)).size, 2);
});

test('station occupancy marks the corresponding half for each direction', () => {
  assert.deepEqual(stationOccupancy(route, [trip({ station: 'b' })]).get('b'), { forward: true, backward: false });
  assert.deepEqual(stationOccupancy(route, [trip({ station: 'b' }, 'a')]).get('b'), { forward: false, backward: true });
});

test('both directions can occupy the two halves independently', () => {
  assert.deepEqual(stationOccupancy(route, [trip({ station: 'b' }), trip({ station: 'b' }, 'a', { id: 2 })]).get('b'),
    { forward: true, backward: true });
});

test('moving trains and trains on other branches do not fill station halves', () => {
  assert.equal(stationOccupancy(route, [trip({ fromStation: 'a', toStation: 'b' }),
    trip({ station: 'b' }, 'd', { routeId: 'branch' })]).size, 0);
});
