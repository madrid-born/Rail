import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../../Rail.WebPages/src/services/itineraryStyle.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const { itineraryRailStyle } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
test('street steps are black and a single metro line fills the circle and connector in its color', () => {
  assert.deepEqual(itineraryRailStyle('#000000'), { '--step-line': '#000000', '--step-dot': '#000000' });
  assert.deepEqual(itineraryRailStyle('#ff0000'), { '--step-line': '#ff0000', '--step-dot': '#ff0000' });
});
test('transfer circles use incoming color above outgoing color and the connector uses the outgoing line', () => {
  const style = itineraryRailStyle('#00ffff', '#ff0000');
  assert.equal(style['--step-line'], '#00ffff');
  assert.equal(style['--step-dot'], 'linear-gradient(to bottom, #ff0000 0%, #ff0000 50%, #00ffff 50%, #00ffff 100%)');
  assert.equal(itineraryRailStyle('#ff0000', '#ff0000')['--step-dot'], '#ff0000');
});

test('boarding from a street section fills the top half black and the bottom half with the metro color', () => {
  const style = itineraryRailStyle('#60c4ef', '#000000');
  assert.equal(style['--step-line'], '#60c4ef');
  assert.equal(style['--step-dot'], 'linear-gradient(to bottom, #000000 0%, #000000 50%, #60c4ef 50%, #60c4ef 100%)');
});

test('alighting shows the metro color above black before the final street section', () => {
  const style = itineraryRailStyle('#000000', '#800080');
  assert.equal(style['--step-line'], '#000000');
  assert.equal(style['--step-dot'], 'linear-gradient(to bottom, #800080 0%, #800080 50%, #000000 50%, #000000 100%)');
});
