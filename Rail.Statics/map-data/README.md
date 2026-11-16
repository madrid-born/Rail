# Rail regional basemap

Installed Android and Windows apps bundle the regional map, fonts and sprites.
MapLibre reads small archive ranges through the native MAUI bridge, without a
server or internet connection. Browser/Vue Dev previews use HTTP byte ranges
from Rail.Api or Nginx. No paid map API or account is required.

## Files

- `tehran-region.pmtiles`: extracted from `https://build.protomaps.com/20261115.pmtiles`.
  Bounds: west 50.5, south 35.3, east 52.0, north 36.2; native zoom levels 0–15.
  Size: 42,943,580 bytes. This includes Tehran, Karaj, Hashtgerd and Parand.
  The archive is ignored by Git; transfer it separately when deploying.
- `fonts/`: self-hosted Protomaps Noto glyphs, including Persian glyphs.
- `sprites/v4/`: light/dark icon sheets matching the Protomaps basemap style.
- `ASSETS-SOURCE.txt`: upstream asset revision. Font and icon licenses are
  retained in `fonts/OFL.txt` and `sprites/LICENSE.md`.

Keep OpenStreetMap and Protomaps attribution visible. The frontend supplies it.
The map file does not contain routing or address-search services.

## Installed app: offline from first launch

Building Rail.Maui includes this archive and its font/icon assets under
`rail-pages/maps/`. The public `network.json` snapshot (151 stations, 9 routes)
is compiled into the frontend, so metro overlays also work on a fresh offline
install. It was exported from the local Rail.Api network endpoint; no account
or timetable data is included. Refresh this snapshot deliberately when releasing
network changes. An online refresh still replaces it using the existing cache.

The native `ReadMapBytes` bridge exposes only this fixed archive and bounds each
read to 256 KiB. On first use, MAUI makes a SHA-256-verified private copy so
Android's potentially non-seekable APK assets can be read efficiently. It does
not load the whole archive into JavaScript memory. This preparation uses about
43 MB of additional app data; obsolete archive copies are removed after an app
update successfully prepares its replacement. A build-generated hash changes
automatically when the map file changes.

The map ignores `VITE_MAP_BASE_URL` inside the installed HybridWebView. It uses
local glyphs and icons even if the API is unreachable. There is no first-run map
download. Map updates currently ship through app updates. The native build fails
with an actionable error if the ignored PMTiles archive is missing.

Offline map display does not enable offline street routing, live departures,
station-detail API responses, account features or timetable journey searches.

## Windows development

Start `Rail.Api` using its `https` launch profile, then run `npm run dev` in
`Rail.WebPages`. The API reads this directory directly in Development and Vite
proxies `/api/maps`. The normal Windows Machine profile uses the bundled offline
map; Windows Machine (Vue Dev) uses the browser preview and its HTTP map host.

The API supports GET/HEAD, ranges, ETags and allowed WebView origins. Configure
`Maps__DataPath` with an absolute path to override the asset directory.
In Production its default directory is `map-data` under the API content root.
Assets are bundled into MAUI automatically, but are not included in API publish.

On Android, set `VITE_API_BASE_URL` to a reachable, trusted HTTPS API address
for online features before building; localhost refers to the Android device.
This URL is not needed for offline map display. `VITE_MAP_BASE_URL` selects a
separate host only for browser/Vue Dev previews.

## Optional Ubuntu x64 browser-map hosting

These assets are platform independent. Copy the contents of this directory,
including the ignored archive, to `/srv/rail/map-data/`. No PMTiles process,
Java runtime or map database is needed to serve them.

Either set `Maps__DataPath=/srv/rail/map-data` on Rail.Api, or serve the files
directly from Nginx. The following location belongs inside the existing HTTPS
server block, alongside the API proxy configuration:

```nginx
location /api/maps/ {
    alias /srv/rail/map-data/;
    autoindex off;
    gzip off;
    etag on;
    expires 1h;
    types {
        application/octet-stream pmtiles;
        application/x-protobuf pbf;
        application/json json;
        image/png png;
    }
    # Public map assets only: no account data or credentials on this location.
    add_header Access-Control-Allow-Origin "*" always;
    add_header Access-Control-Allow-Methods "GET, HEAD, OPTIONS" always;
    add_header Access-Control-Allow-Headers "Range, If-Match, If-None-Match" always;
    add_header Access-Control-Expose-Headers "ETag, Accept-Ranges, Content-Range, Last-Modified" always;
    if ($request_method = OPTIONS) { return 204; }
}
```

Nginx serves ranges automatically; do not force a fake `Accept-Ranges` header
on a server that does not implement them. Check the real response:

```bash
curl -I https://YOUR_HOST/api/maps/tehran-region.pmtiles
curl -sS -D - -o /dev/null -H 'Range: bytes=0-126' \
  https://YOUR_HOST/api/maps/tehran-region.pmtiles
```

Expect `200` for HEAD and `206 Partial Content` for the range, with
`Content-Range: bytes 0-126/42943580`. A range request should not send the whole
43 MB file. The hostname must match `VITE_API_BASE_URL`, or set
`VITE_MAP_BASE_URL=https://YOUR_HOST/api/maps` before building the frontend.

## Updating the map

Use a recent build URL from https://maps.protomaps.com/builds/:

```powershell
.\pmtiles.exe extract "ACTUAL_BUILD_URL" .\tehran-region.pmtiles --bbox=50.5,35.3,52.0,36.2
.\pmtiles.exe verify .\tehran-region.pmtiles
```

No maxzoom argument is used: all available native detail is retained. Extract
to a staging directory and replace the hosted file atomically after verification.
Keep the matching style, sprites and fonts when upgrading the basemap schema.
The frontend bounds in `Rail.WebPages/src/services/map.ts` must change if a new
region is extracted. Local disk space and server bandwidth are still required.
