import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { createServer } from 'vite';

test('local map files support byte ranges while the configured API is unreachable', async () => {
  const server = await createServer({
    configFile: fileURLToPath(new URL('../vite.config.ts', import.meta.url)),
    logLevel: 'silent',
    server: {
      host: '127.0.0.1', port: 0, strictPort: false, watch: null,
      proxy: { '/api': { target: 'http://127.0.0.1:1' } },
    },
  });
  try {
    await server.listen();
    const { port } = server.httpServer.address();
    const base = `http://127.0.0.1:${port}`;
    const archive = await fetch(`${base}/maps/tehran-region.pmtiles`, { headers: { Range: 'bytes=0-126' } });
    assert.equal(archive.status, 206);
    const bytes = Buffer.from(await archive.arrayBuffer());
    assert.equal(bytes.length, 127);
    assert.equal(bytes.subarray(0, 7).toString(), 'PMTiles');
    assert.match(archive.headers.get('content-range'), /^bytes 0-126\/\d+$/);
    const font = await fetch(`${base}/maps/fonts/Noto%20Sans%20Regular/1536-1791.pbf`);
    assert.equal(font.status, 200);
    assert.ok((await font.arrayBuffer()).byteLength > 0);
    const sprite = await fetch(`${base}/maps/sprites/v4/light.json`);
    assert.equal(sprite.status, 200);
    assert.ok(Object.keys(await sprite.json()).length > 0);
    const head = await fetch(`${base}/maps/tehran-region.pmtiles`, { method: 'HEAD' });
    assert.equal(head.status, 200);
    assert.ok(Number(head.headers.get('content-length')) > 127);
  } finally { await server.close(); }
});
