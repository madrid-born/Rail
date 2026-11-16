import type { RangeResponse, Source } from 'pmtiles';

export interface MapAssetBridge { InvokeDotNet(method: string, args?: unknown[]): Promise<unknown> }

// Read only the requested bytes through MAUI, never fetch the 43 MB archive
// through HybridWebView (its packaged-file handler does not promise HTTP ranges).
export class NativeMapSource implements Source {
  constructor(private readonly key: string, private readonly native: MapAssetBridge) {}
  getKey() { return this.key; }

  async getBytes(offset: number, length: number, signal?: AbortSignal): Promise<RangeResponse> {
    if (!Number.isSafeInteger(offset) || offset < 0 || !Number.isSafeInteger(length) || length <= 0
        || !Number.isSafeInteger(offset + length)) throw new RangeError('Invalid map byte range.');
    signal?.throwIfAborted();
    const data = new Uint8Array(length);
    for (let written = 0; written < length;) {
      signal?.throwIfAborted();
      const count = Math.min(length - written, 256 * 1024);
      const encoded = await this.native.InvokeDotNet('ReadMapBytes', [offset + written, count]);
      signal?.throwIfAborted();
      if (typeof encoded !== 'string') throw new Error('Invalid packaged map response.');
      const decoded = atob(encoded);
      if (decoded.length !== count) throw new Error('Incomplete packaged map response.');
      for (let i = 0; i < count; i++) data[written + i] = decoded.charCodeAt(i);
      written += count;
    }
    return { data: data.buffer };
  }
}
