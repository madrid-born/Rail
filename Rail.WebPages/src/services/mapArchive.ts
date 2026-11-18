import { PMTiles } from 'pmtiles';
import { bridge } from './auth';
import { NativeMapSource } from './nativeMapSource';

export const mapAssetBase = new URL('/maps/', window.location.href).href;
export const mapArchiveUrl = `${mapAssetBase}tehran-region.pmtiles`;
const bundledApp = ['appdir', '0.0.0.1', 'appassets.androidplatform.net'].includes(window.location.hostname);
let pending: Promise<PMTiles> | undefined;

// Share the reader and its range cache between the basemap and endpoint labels.
export function getMapArchive(): Promise<PMTiles> {
  pending ??= (async () => {
    if (!bundledApp) return new PMTiles(mapArchiveUrl);
    const native = await bridge();
    if (!native) throw new Error('The packaged map bridge is unavailable.');
    return new PMTiles(new NativeMapSource(mapArchiveUrl, native));
  })().catch(error => { pending = undefined; throw error; });
  return pending;
}
