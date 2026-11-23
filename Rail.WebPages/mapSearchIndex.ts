import { readFile } from 'node:fs/promises';
import { gzipSync } from 'node:zlib';
import { VectorTile } from '@mapbox/vector-tile';
import type { VectorTileFeature } from '@mapbox/vector-tile';
import { PbfReader } from 'pbf';
import { PMTiles } from 'pmtiles';
import type { Plugin } from 'vite';
import type { MapSearchEntry, MapPlaceKind } from './src/services/mapSearch';

const moduleId = 'virtual:rail-map-search-index';
const resolvedId = `\0${moduleId}`;
const devUrl = '/__rail_map_search_index.bin';
const archivePath = new URL('../Rail.Resource/Data/tehran-region.pmtiles', import.meta.url);
const placeDetails = ['city', 'town', 'village', 'hamlet'];
const publicLandmarks = new Set(['attraction', 'historic', 'monument', 'museum', 'park', 'nature_reserve', 'university', 'hospital', 'mall', 'marketplace', 'theatre']);

export function railMapSearchIndex(): Plugin {
  let development = false;
  return {
    name: 'rail-map-search-index',
    resolveId(id) { if (id === moduleId) return resolvedId; },
    async load(id) {
      if (id !== resolvedId) return;
      if (development) return `export default ${JSON.stringify(devUrl)};`;
      const index = await buildIndex();
      if (index === undefined) return 'export default undefined;';
      // Keep the compressed payload opaque to static servers. The client decodes it.
      const asset = this.emitFile({ type: 'asset', name: 'map-search.bin', source: index });
      return `export default import.meta.ROLLUP_FILE_URL_${asset};`;
    },
    configureServer(server) {
      development = true;
      let pending: Promise<Buffer | undefined> | undefined;
      server.middlewares.use(devUrl, (_request, response) => {
        pending ??= buildIndex().catch(error => { pending = undefined; throw error; });
        void pending.then(index => {
          if (index === undefined) pending = undefined;
          response.statusCode = index === undefined ? 404 : 200;
          response.setHeader('Content-Type', 'application/octet-stream');
          response.end(index);
        }, error => {
          server.config.logger.error(String(error));
          response.statusCode = 500;
          response.end();
        });
      });
    },
  };
}

function featureKind(layer: string, feature: VectorTileFeature, names: readonly string[]): MapPlaceKind | undefined {
  const { kind, kind_detail, is_link } = feature.properties;
  if (layer === 'roads') return feature.type === 2 && kind !== 'rail' && !is_link ? 'street' : undefined;
  if (feature.type !== 1) return;
  if (layer === 'places') return placeDetails.includes(String(kind_detail)) ? 'locality' : 'neighborhood';
  if (kind === 'stop' || kind === 'subway_entrance') return;
  if (kind === 'station') {
    // The archive uses the same kind for metro and mainline railway stations.
    const metro = names.some(name => /مترو|\b(?:metro|subway)\b/iu.test(name));
    const railway = names.some(name => /ایستگاه\s+راه[\s‌-]*[آا]هن|\b(?:railway|railroad|train) station\b/iu.test(name));
    return railway && !metro ? 'landmark' : undefined;
  }
  if (kind === 'bus_station' || kind === 'bus_stop') return 'bus';
  if (kind === 'administrative') return 'neighborhood';
  return 'landmark';
}

function importance(kind: MapPlaceKind, properties: VectorTileFeature['properties'], zoom: number) {
  const { kind: detail, min_zoom, wikidata } = properties;
  const labelZoom = typeof min_zoom === 'number' ? min_zoom : zoom;
  const local = Math.max(0, labelZoom - 10) / 2;
  switch (kind) {
    case 'locality': return Math.max(0, labelZoom - 5) / 2;
    case 'neighborhood': return 3 + local;
    case 'street': return (detail === 'major_road' ? 2 : detail === 'medium_road' ? 5 : 9) + local;
    case 'bus': return (detail === 'bus_station' ? 5 : 12) + local;
    case 'landmark': return (wikidata || detail === 'station' || publicLandmarks.has(String(detail)) ? 2 : 9) + local;
  }
}

// Choose a point on a street segment inside the tile. A separate point per tile
// lets search pick a nearby part of a long street instead of a single distant center.
function featurePoint(feature: VectorTileFeature): [number, number] | undefined {
  const geometry = feature.loadGeometry();
  const inside = (x: number, y: number) => x >= 0 && x < feature.extent && y >= 0 && y < feature.extent;
  if (feature.type === 1) {
    const point = geometry[0]?.[0];
    if (point && inside(point.x, point.y)) return [point.x, point.y];
    return;
  }
  let best: [number, number] | undefined;
  let longest = 0;
  for (const line of geometry) {
    for (let i = 1; i < line.length; i++) {
      const a = line[i - 1]!, b = line[i]!;
      const x = (a.x + b.x) / 2, y = (a.y + b.y) / 2;
      const length = (b.x - a.x) ** 2 + (b.y - a.y) ** 2;
      if (length > longest && inside(x, y)) { longest = length; best = [x, y]; }
    }
  }
  return best;
}

async function buildIndex(): Promise<Buffer | undefined> {
  const bytes = await readFile(archivePath).catch((error: NodeJS.ErrnoException) => {
    if (error.code !== 'ENOENT') throw error;
  });
  if (!bytes) return;
  const archive = new PMTiles({
    getKey: () => archivePath.href,
    async getBytes(offset, length) {
      const start = bytes.byteOffset + offset;
      return { data: bytes.buffer.slice(start, start + Math.min(length, bytes.length - offset)) };
    },
  });
  const header = await archive.getHeader();
  const entries = new Map<string, MapSearchEntry>();
  const seenPoints = new Set<string>();
  // Read the detailed tiles first, then fill in labels present only at lower zooms.
  for (let z = header.maxZoom; z >= header.minZoom; z--) {
    const size = 2 ** z;
    const tileX = (lon: number) => (lon + 180) / 360 * size;
    const tileY = (lat: number) => (1 - Math.asinh(Math.tan(lat * Math.PI / 180)) / Math.PI) / 2 * size;
    for (let x = Math.floor(tileX(header.minLon)); x <= Math.floor(tileX(header.maxLon)); x++) {
      for (let y = Math.floor(tileY(header.maxLat)); y <= Math.floor(tileY(header.minLat)); y++) {
        const tile = await archive.getZxy(z, x, y);
        if (!tile) continue;
        const layers = new VectorTile(new PbfReader(tile.data)).layers;
        const streets = new Set<string>();
        for (const layerName of ['places', 'pois', 'water', 'roads']) {
          if (layerName === 'roads' && z !== header.maxZoom) continue;
          const layer = layers[layerName];
          if (!layer) continue;
          for (let i = 0; i < layer.length; i++) {
            const feature = layer.feature(i);
            const props = feature.properties;
            const names = [...new Set([props['name:fa'], props.name, props['name:en'], props['name:ar']]
              .filter((name): name is string => typeof name === 'string' && name.trim() !== '').map(name => name.trim()))];
            if (!names.length) continue;
            const kind = featureKind(layerName, feature, names);
            if (!kind) continue;
            const key = feature.id === undefined ? undefined : `${layerName}/${feature.id}/${names[0]}`;
            const namesKey = JSON.stringify(names);
            if (kind === 'street' ? streets.has(namesKey) : key && seenPoints.has(key)) continue;
            const point = featurePoint(feature);
            if (!point) continue;
            const lon = ((x + point[0] / feature.extent) / size) * 360 - 180;
            const lat = Math.atan(Math.sinh(Math.PI * (1 - 2 * (y + point[1] / feature.extent) / size))) * 180 / Math.PI;
            if (lon < header.minLon || lon > header.maxLon || lat < header.minLat || lat > header.maxLat) continue;
            if (kind === 'street') streets.add(namesKey);
            else if (key) seenPoints.add(key);
            const rank = importance(kind, props, z);
            const entryKey = `${kind}/${rank}/${namesKey}`;
            let entry = entries.get(entryKey);
            if (!entry) {
              entry = [names[0]!, kind, rank, names.slice(1), []];
              entries.set(entryKey, entry);
            }
            entry[4].push(+lon.toFixed(5), +lat.toFixed(5));
          }
        }
      }
    }
  }
  return gzipSync(JSON.stringify([...entries.values()]));
}
