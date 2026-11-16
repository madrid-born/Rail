import * as sdk from 'maplibre-gl';
import type { StyleSpecification } from 'maplibre-gl';
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url';
import { PMTiles, Protocol } from 'pmtiles';
import { layers, namedFlavor } from '@protomaps/basemaps';
import { apiBase } from './api';
import { bridge } from './auth';
import { NativeMapSource } from './nativeMapSource';
import 'maplibre-gl/dist/maplibre-gl.css';

// This module is shared by both lazy-loaded map views. Register once, and bundle
// the worker locally so MAUI never needs a CDN or an external script loader.
sdk.setWorkerUrl(workerUrl);
const protocol = new Protocol();
sdk.addProtocol('pmtiles', protocol.tile);

const bundledApp = ['appdir', '0.0.0.1', 'appassets.androidplatform.net'].includes(window.location.hostname);
const assetBase = bundledApp
  ? new URL('/maps/', window.location.href).href
  : new URL(`${(import.meta.env.VITE_MAP_BASE_URL ?? `${apiBase}/api/maps`).replace(/\/+$/, '')}/`, window.location.href).href;
const archiveUrl = `${assetBase}tehran-region.pmtiles`;
let localArchiveReady: Promise<void> | undefined;

export const mapBounds: sdk.LngLatBoundsLike = [[50.5, 35.3], [52.0, 36.2]];
export { sdk };

export async function createMapStyle(): Promise<StyleSpecification> {
  if (bundledApp) {
    localArchiveReady ??= (async () => {
      const native = await bridge();
      if (!native) throw new Error('The packaged map bridge is unavailable.');
      protocol.add(new PMTiles(new NativeMapSource(archiveUrl, native)));
    })().catch(error => { localArchiveReady = undefined; throw error; });
    await localArchiveReady;
  }
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
