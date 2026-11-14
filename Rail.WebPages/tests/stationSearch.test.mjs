import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';

const source = await readFile(new URL('../src/services/stationSearch.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { searchStations, stationSearchWords } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const stations = [
  { id: 'beheshti', nameFa: 'شهید بهشتی', nameEn: 'Shahid Beheshti' },
  { id: 'theatre', nameFa: 'تئاتر شهر', nameEn: 'Teatr-e Shahr' },
  { id: 'vali', nameFa: 'میدان حضرت ولی‌عصر', nameEn: 'Meydan-e Hazrat Vali-asr' },
  { id: 'fifteen', nameFa: '۱۵ خرداد', nameEn: 'Panzdah-e Khordad' },
];
const find = query => searchStations(stations, stationSearchWords(query)).map(station => station.id);

test('Persian and English queries match word by word regardless of word order', () => {
  assert.deepEqual(find('بهشتی شهید'), ['beheshti']);
  assert.deepEqual(find('BEHESHTI shah'), ['beheshti']);
  assert.deepEqual(find('شهید Beheshti'), ['beheshti']);
});
test('all query words must match the same station', () => {
  assert.deepEqual(find('شهید شهر'), []);
  assert.deepEqual(find('shahid missing'), []);
});
test('partial words, hyphens and punctuation are supported', () => {
  assert.deepEqual(find('teatr shahr'), ['theatre']);
  assert.deepEqual(find('Teatr-e Shahr'), ['theatre']);
});
test('Arabic letter variants, diacritics and Persian half spaces normalize consistently', () => {
  assert.deepEqual(find('وَلِي عصر'), ['vali']);
  assert.deepEqual(find('ولیعصر'), ['vali']);
  assert.deepEqual(find('  حضرت، میدان  '), ['vali']);
});
test('Persian, Arabic and Latin digits match the same station name', () => {
  assert.deepEqual(find('15'), ['fifteen']);
  assert.deepEqual(find('١٥'), ['fifteen']);
  assert.deepEqual(find('۱۵'), ['fifteen']);
});
test('empty searches restore all stations', () => {
  assert.deepEqual(stationSearchWords('   '), []);
  assert.equal(find('').length, stations.length);
});
