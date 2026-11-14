import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';

const source = await readFile(new URL('../src/services/stationBoard.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
  .replace("import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue';", '')
  .replace("import { apiBase } from './api';", "const apiBase = '';");
const { arrivalLabel, segmentProgress, stationDirections, timeLabel, trainLocation, tripKey } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const trip = { id: 17, serviceDate: '2026-11-11', position: { station: null, fromStation: 'a', toStation: 'b' }, stops: [
  { stationId: 'a', departureAt: '2026-11-11T23:58:00+03:30' },
  { stationId: 'b', arrivalAt: '2026-11-12T00:02:00+03:30' },
] };

test('arrival countdown rounds up and handles midnight with absolute timestamps', () => {
  assert.equal(arrivalLabel('2026-11-12T00:02:00+03:30', '2026-11-11T23:59:30+03:30'), '۳ دقیقه دیگر');
  assert.equal(arrivalLabel('2026-11-12T00:02:00+03:30', '2026-11-11T20:32:00Z'), 'در ایستگاه');
});

test('a train dwelling at the station is shown as present until departure', () => {
  assert.equal(arrivalLabel('2026-11-11T10:10:00+03:30', '2026-11-11T10:11:00+03:30', '2026-11-11T10:12:00+03:30'), 'در ایستگاه');
});

test('train positions distinguish a station, a segment, and a future departure', () => {
  const name = id => ({ a: 'A', b: 'B' })[id];
  assert.equal(trainLocation(trip, name), 'بین A و B');
  assert.equal(trainLocation({ ...trip, position: { station: 'a' } }, name), 'در ایستگاه A');
  assert.equal(trainLocation({ ...trip, position: null }, name), 'هنوز حرکت نکرده است');
});

test('train progress uses actual segment duration across midnight, clamped to segment', () => {
  assert.equal(segmentProgress(trip, '2026-11-12T00:01:00+03:30'), .75);
  assert.equal(segmentProgress(trip, '2026-11-11T23:57:00+03:30'), 0);
  assert.equal(segmentProgress(trip, '2026-11-12T00:03:00+03:30'), 1);
  assert.equal(segmentProgress({ ...trip, stops: [] }, '2026-11-12T00:01:00+03:30'), .5);
});

test('repeated daily trip templates retain distinct journey identities', () => {
  assert.notEqual(tripKey(trip), tripKey({ ...trip, serviceDate: '2026-11-12' }));
});

test('arrival clocks always use Tehran time independently of device timezone', () => {
  assert.equal(timeLabel('2026-11-11T20:32:00Z'), '۰۰:۰۲');
});

const route = { id: 'line-4-main', lineId: 4, stops: ['a', 'b', 'c', 'planned'].map((stationId, index) => ({
  stationId, sequence: index + 1, status: stationId === 'planned' ? 'under_construction' : 'operational',
})) };
function boardFor(journeys) {
  return {
    trips: journeys.map(([id, ids]) => ({ id, serviceDate: '2026-11-14', routeId: route.id,
      stops: ids.map((stationId, index) => ({ stationId, sequence: index + 1 })) })),
    nextArrivals: journeys.map(([id, ids, minutes = id]) => ({ tripId: id, serviceDate: '2026-11-14',
      routeId: route.id, directionStationId: ids[ids.length - 1], arrivalAt: `2026-11-14T12:${String(minutes).padStart(2, '0')}:00+03:30` })),
  };
}

test('unfinished route extensions do not hide arrivals toward the operating terminal', () => {
  const board = boardFor([[1, ['a', 'b', 'c']], [2, ['c', 'b', 'a']]]);
  const [forward, backward] = stationDirections(route, new Map(), board);
  assert.equal(forward.id, 'c');
  assert.equal(forward.next.tripId, 1);
  assert.equal(backward.id, 'a');
  assert.equal(backward.next.tripId, 2);
});

test('short-turn services in the same direction choose the earliest train and its actual destination', () => {
  const board = boardFor([[1, ['a', 'b', 'c'], 15], [2, ['a', 'b'], 10], [3, ['c', 'b', 'a'], 12]]);
  const [forward, backward] = stationDirections(route, new Map(), board);
  assert.equal(forward.id, 'b');
  assert.equal(forward.trip.id, 2);
  assert.equal(backward.trip.id, 3);
});

test('empty boards label directions with operating endpoints instead of planned extensions', () => {
  const [forward, backward] = stationDirections(route, new Map());
  assert.equal(forward.id, 'c');
  assert.equal(backward.id, 'a');
  assert.equal(forward.next, undefined);
});

test('other branches and occurrences from different service dates do not supply arrivals', () => {
  const board = boardFor([[1, ['a', 'b', 'c']]]);
  board.trips[0].serviceDate = '2026-11-13';
  assert.equal(stationDirections(route, new Map(), board)[0].next, undefined);
  board.trips[0].serviceDate = '2026-11-14';
  board.nextArrivals[0].routeId = 'branch';
  assert.equal(stationDirections(route, new Map(), board)[0].next, undefined);
});
