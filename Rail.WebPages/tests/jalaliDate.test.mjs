import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';
const source = await readFile(new URL('../src/services/jalaliDate.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { gregorianToJalali, jalaliToGregorian, jalaliMonth } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);

test('Jalali dates round-trip with Persian, Arabic and Latin digits', () => {
  assert.equal(gregorianToJalali('2026-11-14'), '1405/08/23');
  for (const value of ['1405/08/23', '۱۴۰۵/۰۸/۲۳', '١٤٠٥/٠٨/٢٣']) assert.equal(jalaliToGregorian(value), '2026-11-14');
  assert.equal(gregorianToJalali('2024-03-20'), '1403/01/01');
  assert.equal(jalaliToGregorian('1403/1/1'), '2024-03-20');
});
test('validates leap days and rejects dates that would overflow into another month', () => {
  assert.equal(jalaliToGregorian('1399/12/30'), '2021-03-20');
  for (const value of ['1400/12/30', '1405/07/31', '1405/13/1', '1405/0/1', '1405/1/0', '1405/1/32', 'bad', '']) {
    assert.equal(jalaliToGregorian(value), undefined);
  }
  assert.equal(gregorianToJalali('2026-02-30'), '');
});
test('calendar month navigation handles leap Esfand and year changes', () => {
  const esfand = jalaliMonth('2021-03-20');
  assert.equal(esfand.days.length, 30);
  assert.equal(esfand.days[29].date, '2021-03-20');
  const next = jalaliMonth('2021-03-20', 1);
  assert.equal(next.first, '2021-03-21');
  assert.equal(next.days.length, 31);
  assert.equal(gregorianToJalali(next.first), '1400/01/01');
  const previous = jalaliMonth('2021-03-21', -1);
  assert.equal(previous.first, esfand.first);
});
test('calendar weeks start on Saturday and do not depend on the local timezone', () => {
  const month = jalaliMonth('2026-11-14');
  assert.equal(month.weekday, 6); // Aban 1 is Friday; Saturday is column zero.
  assert.equal(month.days[22].date, '2026-11-14');
  assert.equal(month.days.length, 30);
});
