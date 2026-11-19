import assert from 'node:assert/strict';
import { readFile, open } from 'node:fs/promises';
import { test } from 'node:test';
import { PMTiles } from 'pmtiles';
import ts from 'typescript';

const source = await readFile(new URL('../../Rail.WebPages/src/services/nativeMapSource.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { NativeMapSource } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);

test('native reads are chunked, byte-exact, and use only the fixed map bridge', async () => {
  const bytes = Uint8Array.from({ length: 700_000 }, (_, i) => i % 256);
  const calls = [];
  const native = new NativeMapSource('local-map', { InvokeDotNet: async (method, [offset, length]) => {
    calls.push([method, offset, length]);
    return Buffer.from(bytes.slice(offset, offset + length)).toString('base64');
  } });
  assert.deepEqual(new Uint8Array((await native.getBytes(123, 600_000)).data), bytes.slice(123, 600_123));
  assert.equal(calls.length, 3);
  assert.ok(calls.every(([method, , count]) => method === 'ReadMapBytes' && count <= 256 * 1024));
});

test('aborted, invalid and incomplete requests cannot return corrupt map bytes', async () => {
  let calls = 0;
  const native = new NativeMapSource('local-map', { InvokeDotNet: async () => { calls++; return 'AA=='; } });
  await assert.rejects(native.getBytes(-1, 1), RangeError);
  await assert.rejects(native.getBytes(0, 0), RangeError);
  await assert.rejects(native.getBytes(Number.MAX_SAFE_INTEGER, 2), RangeError);
  await assert.rejects(native.getBytes(0, 1, AbortSignal.abort()), { name: 'AbortError' });
  assert.equal(calls, 0);
  await assert.rejects(native.getBytes(0, 2), /Incomplete/);
});

test('PMTiles decodes the real bundled archive through native byte reads without HTTP', async t => {
  const archiveUrl = new URL('../../Rail.Resource/Data/tehran-region.pmtiles', import.meta.url);
  let file;
  try { file = await open(archiveUrl, 'r'); }
  catch (error) { if (error.code === 'ENOENT') return t.skip('Regional archive has not been downloaded.'); throw error; }
  try {
    let transferred = 0;
    const native = new NativeMapSource('offline://tehran', { InvokeDotNet: async (_, [offset, length]) => {
      transferred += length;
      const data = Buffer.alloc(length);
      const result = await file.read(data, 0, length, offset);
      assert.equal(result.bytesRead, length);
      return data.toString('base64');
    } });
    const pmtiles = new PMTiles(native);
    const header = await pmtiles.getHeader();
    assert.equal(header.maxZoom, 15);
    const zoom = 14, longitude = 51.4, latitude = 35.7;
    const x = Math.floor((longitude + 180) / 360 * 2 ** zoom);
    const radians = latitude * Math.PI / 180;
    const y = Math.floor((1 - Math.asinh(Math.tan(radians)) / Math.PI) / 2 * 2 ** zoom);
    const tile = await pmtiles.getZxy(zoom, x, y);
    assert.ok(tile && tile.data.byteLength > 0);
    assert.ok(transferred < 1024 * 1024, `Read ${transferred} bytes for a Tehran tile`);
  } finally { await file.close(); }
});
