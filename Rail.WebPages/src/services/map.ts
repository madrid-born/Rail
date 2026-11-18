import * as sdk from 'maplibre-gl';
import type { StyleSpecification } from 'maplibre-gl';
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url';
import { Protocol } from 'pmtiles';
import { layers, namedFlavor } from '@protomaps/basemaps';
import { getMapArchive, mapAssetBase as assetBase, mapArchiveUrl as archiveUrl } from './mapArchive';
import 'maplibre-gl/dist/maplibre-gl.css';

// This module is shared by both lazy-loaded map views. Register once, and bundle
// the worker locally so MAUI never needs a CDN or an external script loader.
sdk.setWorkerUrl(workerUrl);
const protocol = new Protocol();
sdk.addProtocol('pmtiles', protocol.tile);

export const mapBounds: sdk.LngLatBoundsLike = [[50.5, 35.3], [52.0, 36.2]];
export { sdk };

export async function createMapStyle(): Promise<StyleSpecification> {
  protocol.add(await getMapArchive());
  return {
    version: 8,
    glyphs: `${assetBase}fonts/{fontstack}/{range}.pbf`,
    sprite: `${assetBase}sprites/v4/light`,
    sources: {
      protomaps: {
        type: 'vector',
        url: `pmtiles://${archiveUrl}`,
        attribution: '<a href="https://protomaps.com">Protomaps</a> | © <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
      },
    },
    layers: layers('protomaps', namedFlavor('light'), { lang: 'fa' }) as StyleSpecification['layers'],
  };
}
