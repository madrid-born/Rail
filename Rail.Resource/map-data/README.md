# Rail regional basemap

Installed Android and Windows apps bundle the regional map, fonts and sprites.
MapLibre reads small archive ranges through the native MAUI bridge, without a
server or internet connection. Browser/Vue Dev previews use HTTP byte ranges
served directly by Vite from this local directory. No paid map API or account is required.

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

## Installed app: offline basemap

Building Rail.Maui includes this archive and its font/icon assets under
`rail-pages/maps/`. Station and metro overlays use the Rail.Api network endpoint
and its downloaded local cache. They require one successful API load before
they are available offline; the basemap itself is bundled.

The native `ReadMapBytes` bridge exposes only this fixed archive and bounds each
read to 256 KiB. On first use, MAUI makes a SHA-256-verified private copy so
Android's potentially non-seekable APK assets can be read efficiently. It does
not load the whole archive into JavaScript memory. This preparation uses about
43 MB of additional app data; obsolete archive copies are removed after an app
update successfully prepares its replacement. A build-generated hash changes
automatically when the map file changes.

Map URLs always use the local `/maps/` path, independently of `VITE_API_BASE_URL`. It uses
local glyphs and icons even if the API is unreachable. There is no first-run map
download. Map updates currently ship through app updates. The native build fails
with an actionable error if the ignored PMTiles archive is missing.

Offline map display does not enable offline street routing, live departures,
station-detail API responses, account features or timetable journey searches.

## Windows development

Run `npm run dev` in `Rail.WebPages`; Vite serves this directory at `/maps`,
including byte-range responses. `npm run preview` serves the same local files.
The API can be stopped, unavailable, or set to an older deployment; the map still
loads locally. Only `/api` requests are proxied to `VITE_API_BASE_URL`.

The normal Windows Machine profile uses the bundled offline map; Windows Machine
(Vue Dev) uses Vite's local map files. MAUI packages the map automatically;
no map assets need to be uploaded to the API for installed apps.

Rail.Api retains the optional `/api/maps` static endpoint for compatibility,
but the frontend does not use it. `Maps__DataPath` can override that endpoint's
asset directory. Assets are not included in API publish automatically.

On Android, set `VITE_API_BASE_URL` to a reachable HTTPS API address for online
features before building. This address is not used for offline map display.

## Optional Ubuntu x64 browser-map hosting

These assets are platform independent. Copy the contents of this directory,
including the ignored archive, to `/srv/rail/map-data/`. No PMTiles process,
Java runtime or map database is needed to serve them.

For a standalone browser deployment, serve these files directly from Nginx at
`/maps/` on the frontend host. The following location belongs inside that host's
HTTPS server block. Installed MAUI apps do not need this hosting:

```nginx
location /maps/ {
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
curl -I https://YOUR_FRONTEND_HOST/maps/tehran-region.pmtiles
curl -sS -D - -o /dev/null -H 'Range: bytes=0-126' \
  https://YOUR_FRONTEND_HOST/maps/tehran-region.pmtiles
```

Expect `200` for HEAD and `206 Partial Content` for the range, with
`Content-Range: bytes 0-126/42943580`. A range request should not send the whole
43 MB file. Host these files alongside the browser frontend at `/maps/`; the API domain
can be different. Installed MAUI apps use packaged assets instead.

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
