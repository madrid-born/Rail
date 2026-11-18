import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/journeyChoices.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const { journeyChoices, selectedJourney } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const option = (id, totalSeconds) => ({ id, totalSeconds, legs: [{ fromStationId: id + '-from', toStationId: id + '-to' }], access: { stationId: id + '-from' }, exit: { stationId: id + '-to' } });

test('choices sort by total travel time and default to the fastest route without mutating the response', () => {
  const response = { timeMode: 'arriveBy', viaStationIds: ['required'], options: [option('slow', 900), option('fast', 300), option('middle', 600)] };
  assert.deepEqual(journeyChoices(response).map(c => c.id), ['fast', 'middle', 'slow']);
  assert.deepEqual(response.options.map(c => c.id), ['slow', 'fast', 'middle']);
  const chosen = selectedJourney(response);
  assert.equal(chosen.id, 'fast');
  assert.equal(chosen.totalSeconds, 300);
  assert.equal(chosen.access.stationId, 'fast-from');
  assert.deepEqual(chosen.viaStationIds, ['required']);
});

test('manual choice updates all journey details and survives refresh until the option disappears', () => {
  const response = { directPath: { mode: 'walk' }, options: [option('fast', 300), option('slow', 900)] };
  const chosen = selectedJourney(response, 'slow');
  assert.equal(chosen.id, 'slow');
  assert.equal(chosen.legs[0].fromStationId, 'slow-from');
  assert.equal(chosen.exit.stationId, 'slow-to');
  assert.equal(selectedJourney({ ...response, options: [option('fast', 290), option('slow', 910)] }, chosen.id).id, 'slow');
  assert.equal(selectedJourney({ ...response, options: [option('fast', 290)] }, chosen.id).id, 'fast');
});

test('at most five alternatives accompany the selected route', () => {
  const response = { options: Array.from({ length: 10 }, (_, i) => option(String(i), 1000 - i * 10)) };
  assert.equal(journeyChoices(response).length, 6);
  assert.equal(journeyChoices(response)[0].totalSeconds, 910);
});

test('an older API still displays its one available route with the correct total time', () => {
  const response = { arrivalLocal: '2026-11-18T18:00:00', departureLocal: '2026-11-18T17:30:00', walkOnly: false, legs: [] };
  assert.equal(journeyChoices(response).length, 1);
  assert.equal(selectedJourney(response).totalSeconds, 1800);
});
