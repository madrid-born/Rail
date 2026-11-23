# Self-hosted street routing for Rail

Rail.Api calls the open-source GraphHopper 11.1 service over HTTP. No API key or
paid GraphHopper account is used. The APK's offline PMTiles basemap is separate:
GraphHopper needs routable OpenStreetMap `.osm.pbf` data, not rendered map tiles.

## Windows 11 development

Run this directory's `Start.ps1`. It uses a portable Java runtime under `runtime/`
when present, otherwise `java` on PATH. Java 25 or later is required for 11.1.
For a fresh checkout, prepare these ignored runtime files first:

1. Download a Windows x64 Java 25 JRE from
   https://github.com/adoptium/temurin25-binaries/releases and extract it under
   `runtime/` (or pass `-JavaPath` to `Start.ps1`).
2. Download https://repo.maven.apache.org/maven2/com/graphhopper/graphhopper-web/11.1/graphhopper-web-11.1.jar
   as `Rail.Resource/Data/graphhopper-web-11.1.jar` in the repository.
3. Put the regional extract in `Rail.Resource/Data/tehran-region-261029.osm.pbf`
   (about 26 MB). `Start.ps1` reads the JAR and PBF directly from this Git-ignored folder.
   Override these locations with `-JarPath` and `-DataPath` when needed. It follows the PMTiles
   coverage bounds: west 50.5, south 35.3, east 52.0, north 36.2. Boundary roads
   and their turn restrictions are completed, so some geometry extends beyond
   the rectangle. The PBF and graph cache are ignored by Git.

```powershell
# From the repository root:
.\infrastructure\graphhopper\Start.ps1
```

To reuse a PBF stored elsewhere without copying it, pass its absolute path:

```powershell
.\infrastructure\graphhopper\Start.ps1 -DataPath 'D:\Storage\tehran-region-261029.osm.pbf'
```

If the current terminal still resolves an older Java installation, also pass
`-JavaPath 'C:\Program Files\Java\jdk-25.0.5+8-jre\bin\java.exe'`.

The first launch imports the OSM data for three routing profiles. This
uses a 1 GB maximum Java heap, starting at 128 MB. Subsequent starts reuse
`graph-cache/tehran-region/`, separate from the previous full-Iran graph.
You can override the heap using `-Heap` if needed. Wait for the server to report that it has started before testing.

```powershell
Invoke-RestMethod 'http://127.0.0.1:8989/info'
Invoke-RestMethod 'http://127.0.0.1:8989/route?point=35.69,51.4&point=35.71,51.42&profile=foot&points_encoded=false&locale=fa'
```

Keep GraphHopper running alongside Rail.Api. The API defaults to
`http://127.0.0.1:8989/`, configured in `appsettings.json`; restart an already
running API after updating its routing code. Docker Desktop with Linux containers
can also run the Ubuntu compose setup below on Windows.

## Ubuntu x64 server

With Docker Engine and the Compose plugin installed, copy this directory's
configuration to the server and upload the regional PBF as `data/tehran-region.osm.pbf`:

```bash
cd /path/to/Rail/infrastructure/graphhopper
mkdir -p data graph-cache
# Upload tehran-region-261029.osm.pbf to data/tehran-region.osm.pbf first.
docker compose up -d --build
docker compose logs -f graphhopper
```

The compose file limits total container memory to 1536 MiB with a 1 GB Java heap; leave
additional RAM for Ubuntu, Rail.Api and other services. The image downloads the
official pinned 11.1 JAR and uses a Java 25 runtime. Application port 8989 is
published only on host loopback; the administration port is not published.
Keep the service private behind Rail.Api. It does not need an Nginx public route.

Configure the API process if GraphHopper runs at a different internal address:

```text
GraphHopper__BaseUrl=http://127.0.0.1:8989/
GraphHopper__Profiles__walk=foot
GraphHopper__Profiles__car=car
GraphHopper__Profiles__motorcycle=motorcycle
```

When the API also runs in Docker, use a shared Docker network and the service
address `http://graphhopper:8989/`; localhost inside the API container refers to
that API container. The profile names must match `config.yml`.

## Routing mode

The initial configuration uses flexible routing, with an eight-second engine
request limit. It applies the same profiles, road restrictions and duration model
without contraction-hierarchy (CH) preparation. This reduces setup
cost and fits Rail's short street legs around metro stations. For higher traffic
or long routes, benchmark the regional graph with CH preparations. CH changes query speed, not whether live traffic
is available. Prepare changed graphs in staging before switching the service.

## Travel times and limitations

- Walking, car and motorcycle have separate profiles. Pedestrian paths and
  stairs are imported. Car/motorcycle turn restrictions and U-turn costs are
  enabled. The upstream motorcycle model uses car access/speed data with its own
  preferences; validate local motorcycle restrictions before relying on it.
- Returned distance is in metres. GraphHopper's millisecond durations are rounded
  up to seconds for Rail, including instruction times. GeoJSON coordinates remain
  longitude,latitude; request points are latitude,longitude.
- GraphHopper supplies baseline model times, not live or historical traffic data.
  Rail.Api adjusts car/motorcycle times using the provisional day/hour model in
  [Navigation](../../Rail.Api/NAVIGATION.md). Those responses carry
  `trafficModel: "assumed_day_hour_v1"` and `baselineSeconds`; walking stays unadjusted.
  Only raw baseline paths are cached, so different requested times cannot reuse an
  already traffic-adjusted duration.
- Route responses identify `source: "graphhopper"` and are cached for five minutes,
  separately by direction, mode, configured profile and service address.
- Unreachable/unsnappable endpoints are marked `no_route` and excluded from station
  access candidates. Provider errors/timeouts use the existing explicit rough
  estimates (`estimated`), with no invented street geometry; the UI warns about
  these missing routes. Caller cancellation is respected.
- Rail's metro journey planner continues to use the database timetable. Street
  routing remains server-side and needs a phone connection to Rail.Api; bundling
  the basemap does not bundle this Java routing engine into the APK.

## Updating road data or profiles

Stop the service before replacing road data. Prepare a new graph in a separate
staging directory and verify it before switching the live service. An existing
graph cache does not automatically reimport a changed PBF; profiles, encoded
values and GraphHopper versions must match the prepared graph. Keep the previous
data/cache until the replacement has been validated. Respect OpenStreetMap
attribution and data licence requirements.

Upstream documentation: https://github.com/graphhopper/graphhopper/tree/11.x/docs

Changing `-DataPath` alone does not rebuild an existing graph. For a different
region or replacement dataset, prepare a new `graph.location` and validate it
before switching. The previous full-Iran cache is retained for rollback.

## Regional memory smoke check (Windows, 2026-11-19)

The Tehran extract imported successfully with `-Xms128m -Xmx1g`, producing
413,905 routing nodes and 525,985 edges. All three profiles returned geometry;
a batch of 12 short routes with concurrency four succeeded. After this batch,
the Windows process working set was about 287 MiB and private memory 364 MiB.
These are a small local smoke check, not a peak-memory or production-load guarantee.
Linux/container memory must be measured separately. Docker's 1536 MiB cap includes
more than the Java heap; the Windows script limits only the heap, not process RAM.

## Remote router during local development

The deployed router is available at `https://graph-hopper.kofi.rest/`.
Local `Rail.Api/appsettings.Development.json` uses this as `GraphHopper:BaseUrl`;
restart Rail.Api after changing its configuration. No SSH tunnel is required.
Nginx exposes GET `/route` and `/info` over HTTPS, with other paths returning 404.
The underlying Docker port stays bound to server loopback. This endpoint currently
has no authentication. Server-side Rail.Api can continue using the internal URL
`http://127.0.0.1:8989/` when running directly on the same host.
