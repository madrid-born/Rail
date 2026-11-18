import { VectorTile } from '@mapbox/vector-tile';
import { PbfReader } from 'pbf';
import type { PMTiles } from 'pmtiles';
import type { Feature, Geometry, Position } from 'geojson';
import type { MapPoint } from './journey';
import { getMapArchive } from './mapArchive';

type NamedFeature = { layer: string; feature: Feature<Geometry> };
type Candidate = { name: string; distance: number };

function distanceToGeometry(point: MapPoint, geometry: Geometry): number {
  const scale = Math.cos(point[1] * Math.PI / 180);
  const project = (p: Position) => [(p[0]! - point[0]) * 111320 * scale, (p[1]! - point[1]) * 111320];
  const distanceToLine = (line: Position[]) => {
    let distance = Infinity;
    for (let i = 1; i < line.length; i++) {
      const [ax, ay] = project(line[i - 1]!) as MapPoint;
      const [bx, by] = project(line[i]!) as MapPoint;
      const dx = bx - ax, dy = by - ay;
      const t = Math.max(0, Math.min(1, -(ax * dx + ay * dy) / (dx * dx + dy * dy || 1)));
      distance = Math.min(distance, Math.hypot(ax + t * dx, ay + t * dy));
    }
    return distance;
  };
  switch (geometry.type) {
    case 'LineString': return distanceToLine(geometry.coordinates);
    case 'MultiLineString': return Math.min(...geometry.coordinates.map(distanceToLine));
    default: return Infinity;
  }
}

export function localPlaceTitle(point: MapPoint, features: NamedFeature[]): string | undefined {
  const mainRoads: Candidate[] = [], sideRoads: Candidate[] = [];
  for (const { layer, feature } of features) {
    const properties = feature.properties ?? {};
    // Keep the main street first and a nearby smaller street second; never use POIs or ramps.
    if (layer !== 'roads' || properties.is_link) continue;
    const main = ['primary', 'secondary', 'tertiary'].includes(properties.kind_detail);
    const side = ['residential', 'unclassified', 'living_street', 'service'].includes(properties.kind_detail);
    if (!main && !side) continue;
    const name = [properties['name:fa'], properties.name, properties['name:en']]
      .find(value => typeof value === 'string' && value.trim())?.trim();
    if (!name || !feature.geometry) continue;
    (main ? mainRoads : sideRoads).push({ name, distance: distanceToGeometry(point, feature.geometry) });
  }
  const nearest = (roads: Candidate[], limit: number) => roads.filter(road => road.distance <= limit)
    .sort((a, b) => a.distance - b.distance || a.name.localeCompare(b.name, 'fa'))[0];
  const main = nearest(mainRoads, 1000);
  const side = nearest(sideRoads.filter(road => road.name !== main?.name), 250);
  return [main?.name, side?.name].filter(Boolean).join('، ') || undefined;
}

export function createLocalPlaceLookup(loadArchive: () => Promise<PMTiles>) {
  const tileCache = new Map<string, NamedFeature[]>();
  return async (point: MapPoint, signal?: AbortSignal): Promise<string | undefined> => {
    signal?.throwIfAborted();
    if (!Number.isFinite(point[0]) || !Number.isFinite(point[1]) || Math.abs(point[1]) > 85.051129) return;
    const archive = await loadArchive();
    const header = await archive.getHeader();
    signal?.throwIfAborted();
    if (point[0] < header.minLon || point[0] > header.maxLon || point[1] < header.minLat || point[1] > header.maxLat) return;
    const z = header.maxZoom, size = 2 ** z;
    const x = Math.floor((point[0] + 180) / 360 * size);
    const y = Math.floor((1 - Math.asinh(Math.tan(point[1] * Math.PI / 180)) / Math.PI) / 2 * size);
    const features: NamedFeature[] = [];
    // Include neighboring tiles so street selection is independent of tile boundaries and map zoom.
    for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++) {
      signal?.throwIfAborted();
      const tx = x + dx, ty = y + dy;
      if (tx < 0 || ty < 0 || tx >= size || ty >= size) continue;
      const key = `${z}/${tx}/${ty}`;
      let decoded = tileCache.get(key);
      if (!decoded) {
        const tile = await archive.getZxy(z, tx, ty, signal);
        signal?.throwIfAborted();
        decoded = [];
        if (tile) {
          const vector = new VectorTile(new PbfReader(tile.data));
          for (const name of ['roads']) {
            const layer = vector.layers[name];
            if (!layer) continue;
            for (let i = 0; i < layer.length; i++) {
              const feature = layer.feature(i);
              const p = feature.properties;
              if (p.name || p['name:fa'] || p['name:en']) decoded.push({ layer: name, feature: feature.toGeoJSON(tx, ty, z) });
            }
          }
        }
        if (tileCache.size >= 64) tileCache.delete(tileCache.keys().next().value!);
        tileCache.set(key, decoded);
      }
      features.push(...decoded);
    }
    signal?.throwIfAborted();
    return localPlaceTitle(point, features);
  };
}

export const findLocalPlace = createLocalPlaceLookup(getMapArchive);
