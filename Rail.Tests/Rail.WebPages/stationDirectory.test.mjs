import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';

const source = await readFile(new URL('../../Rail.WebPages/src/services/stationDirectory.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { stationDirectory } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);

const membership = (lineId, isOperational = true) => ({ lineId, isOperational });
const station = (id, lines, infrastructureStatus = 'operational') => ({ id, lines, infrastructureStatus });
const stop = (stationId, status = 'operational') => ({ stationId, status });

test('the operating directory hides unavailable lines, branches, stops and transfer memberships', () => {
  const network = {
    lines: [
      { id: 1 }, { id: 2, infrastructureStatus: 'operational' }, { id: 3, infrastructureStatus: 'temporarily_closed' },
      ...[8, 9, 10, 11].map(id => ({ id, infrastructureStatus: 'under_construction' })),
    ],
    stations: [
      station('junction', [membership(1), membership(2), membership(3), membership(8, false)]),
      station('shared', [membership(1), membership(2)]),
      station('vavan', [membership(1)], 'under_construction'),
      station('closed', [membership(1)], 'temporarily_closed'),
      station('inactive-platform', [membership(1, false), membership(2)]),
      station('airport', [membership(1)]),
      station('future', [membership(8, false)], 'under_construction'),
    ],
    routes: [
      { id: 'main', lineId: 1, branchCode: null, stops: [stop('junction'), stop('shared'), stop('closed'), stop('inactive-platform')] },
      { id: 'airport', lineId: 1, branchCode: 'airport', stops: [stop('junction'), stop('vavan'), stop('airport')] },
      { id: 'planned-branch', lineId: 1, branchCode: 'planned', stops: [stop('junction'), stop('shared', 'planned')] },
      { id: 'second', lineId: 2, branchCode: null, stops: [stop('junction', 'planned'), stop('shared'), stop('inactive-platform')] },
      { id: 'expansion', lineId: 8, branchCode: null, stops: [stop('junction'), stop('future', 'under_construction')] },
    ],
  };
  const original = structuredClone(network);
  const visible = stationDirectory(network, false);

  assert.deepEqual(visible.lines.map(line => line.id), [1, 2]);
  assert.deepEqual(visible.routes.map(route => [route.id, route.stops.map(stop => stop.stationId)]), [
    ['main', ['junction', 'shared']],
    ['airport', ['junction', 'airport']],
    ['second', ['shared', 'inactive-platform']],
  ]);
  assert.deepEqual(visible.stations.map(station => [station.id, station.lines.map(line => line.lineId)]), [
    ['junction', [1]], ['shared', [1, 2]], ['inactive-platform', [2]], ['airport', [1]],
  ]);
  assert.deepEqual(stationDirectory(network, true), original);
  assert.deepEqual(network, original);
});
