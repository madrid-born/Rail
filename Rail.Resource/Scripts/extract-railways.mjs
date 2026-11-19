// Export the basemap's railway geometry, preserving tile fragments and buffers.
// Run from any directory: node Rail.Resource/Scripts/extract-railways.mjs
import { open, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { PMTiles } from 'pmtiles';
import { VectorTile } from '@mapbox/vector-tile';
import { PbfReader } from 'pbf';

const input = fileURLToPath(new URL('../../Rail.Resource/Data/tehran-region.pmtiles', import.meta.url));
const output = process.argv[2] ?? fileURLToPath(new URL('../../Rail.Resource/map-data/tehran-railways.geojson', import.meta.url));
const file = await open(input, 'r');
try {
  const archive = new PMTiles({
    getKey: () => input,
    async getBytes(offset, length) {
      const buffer = Buffer.alloc(length);
      const { bytesRead } = await file.read(buffer, 0, length, offset);
      return { data: buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + bytesRead) };
    },
  });
  const header = await archive.getHeader();
  const z = header.maxZoom;
  const tileX = lon => Math.floor((lon + 180) / 360 * 2 ** z);
  const tileY = lat => Math.floor((1 - Math.asinh(Math.tan(lat * Math.PI / 180)) / Math.PI) / 2 * 2 ** z);
  const features = [];
  const counts = {};
  let tiles = 0;
  for (let x = tileX(header.minLon); x <= tileX(header.maxLon); x++) {
    for (let y = tileY(header.maxLat); y <= tileY(header.minLat); y++) {
      const tile = await archive.getZxy(z, x, y);
      if (!tile) continue;
      tiles++;
      const roads = new VectorTile(new PbfReader(new Uint8Array(tile.data))).layers.roads;
      if (!roads) continue;
      for (let i = 0; i < roads.length; i++) {
        const feature = roads.feature(i);
        if (feature.type !== 2 || feature.properties.kind !== 'rail') continue;
        const geojson = feature.toGeoJSON(x, y, z);
        geojson.properties.source_tile = `${z}/${x}/${y}`;
        features.push(geojson);
        const kind = feature.properties.kind_detail ?? 'unspecified';
        counts[kind] = (counts[kind] ?? 0) + 1;
      }
    }
  }
  await writeFile(output, JSON.stringify({
    type: 'FeatureCollection',
    name: 'Tehran railway tile fragments',
    attribution: '© OpenStreetMap contributors; Protomaps basemap',
    description: 'Zoom 15 railway centerlines. Tile fragments and overlapping tile buffers are preserved. Not a connected routing network or survey geometry.',
    features,
  }));
  console.log(JSON.stringify({ output, zoom: z, tiles, features: features.length, counts }, null, 2));
} finally {
  await file.close();
}
// Export the basemap's railway geometry, preserving tile fragments and buffers.
// Run from any directory: node Rail.Resource/Scripts/extract-railways.mjs
import { open, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { PMTiles } from 'pmtiles';
import { VectorTile } from '@mapbox/vector-tile';
import { PbfReader } from 'pbf';

const input = fileURLToPath(new URL('../../Rail.Resource/Data/tehran-region.pmtiles', import.meta.url));
const output = process.argv[2] ?? fileURLToPath(new URL('../../Rail.Resource/map-data/tehran-railways.geojson', import.meta.url));
const file = await open(input, 'r');
try {
  const archive = new PMTiles({
    getKey: () => input,
    async getBytes(offset, length) {
      const buffer = Buffer.alloc(length);
      const { bytesRead } = await file.read(buffer, 0, length, offset);
      return { data: buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + bytesRead) };
    },
  });
  const header = await archive.getHeader();
  const z = header.maxZoom;
  const tileX = lon => Math.floor((lon + 180) / 360 * 2 ** z);
  const tileY = lat => Math.floor((1 - Math.asinh(Math.tan(lat * Math.PI / 180)) / Math.PI) / 2 * 2 ** z);
  const features = [];
  const counts = {};
  let tiles = 0;
  for (let x = tileX(header.minLon); x <= tileX(header.maxLon); x++) {
    for (let y = tileY(header.maxLat); y <= tileY(header.minLat); y++) {
      const tile = await archive.getZxy(z, x, y);
      if (!tile) continue;
      tiles++;
      const roads = new VectorTile(new PbfReader(new Uint8Array(tile.data))).layers.roads;
      if (!roads) continue;
      for (let i = 0; i < roads.length; i++) {
        const feature = roads.feature(i);
        if (feature.type !== 2 || feature.properties.kind !== 'rail') continue;
        const geojson = feature.toGeoJSON(x, y, z);
        geojson.properties.source_tile = `${z}/${x}/${y}`;
        features.push(geojson);
        const kind = feature.properties.kind_detail ?? 'unspecified';
        counts[kind] = (counts[kind] ?? 0) + 1;
      }
    }
  }
  await writeFile(output, JSON.stringify({
    type: 'FeatureCollection',
    name: 'Tehran railway tile fragments',
    attribution: '© OpenStreetMap contributors; Protomaps basemap',
    description: 'Zoom 15 railway centerlines. Tile fragments and overlapping tile buffers are preserved. Not a connected routing network or survey geometry.',
    features,
  }));
  console.log(JSON.stringify({ output, zoom: z, tiles, features: features.length, counts }, null, 2));
} finally {
  await file.close();
}
