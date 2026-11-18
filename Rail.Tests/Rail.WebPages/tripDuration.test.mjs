import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/tripDuration.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const { tripDuration } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
test('travel durations show Persian hours and minutes without seconds', () => {
  assert.equal(tripDuration(0), '۰ دقیقه');
  assert.equal(tripDuration(61), '۲ دقیقه');
  assert.equal(tripDuration(3599), '۱ ساعت');
  assert.equal(tripDuration(3661), '۱ ساعت و ۲ دقیقه');
  assert.equal(tripDuration(7200), '۲ ساعت');
});
