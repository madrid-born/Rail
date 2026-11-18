# Railway paths

`TrackSegments.Id` is an integer identity. `SourceId` retains the original unique
string ID. The migration preserves existing connections. New connections should
provide a stable SourceId and let SQL Server assign Id.

`Metro.TrackSegmentPoints` has primary key `(TrackSegmentId, Sequence)` with
zero-based sequence and decimal(10,7) latitude/longitude. Points run from
FromStation to ToStation. Parallel tracks produce one chosen path, shared in
reverse when the same line has the reverse connection. Station markers retain
their original coordinates; the path endpoints are projected onto the track.

Run from the repository root:

```powershell
dotnet run --project Rail.Resource -- migrate
dotnet run --project Rail.Resource -- export Rail.Resource/map-data/track-connections.json
node Rail.Resource/Scripts/extract-railways.mjs
python Rail.Resource/Scripts/build-track-paths.py Rail.Resource/map-data/track-connections.json Rail.Resource/map-data/tehran-railways.geojson Rail.Resource/map-data/track-paths.json
dotnet run --project Rail.Resource -- import Rail.Resource/map-data/track-paths.json
dotnet run --project Rail.Resource -- verify
```

The CLI uses Rail.Api development configuration, user secrets and environment
overrides. Import validates segment identities and coordinates before replacing
only included paths in one transaction. Unmatched geometry is preserved.

The generated `.report.json` lists snap distances, path lengths and unmatched
connections; `.geojson` supports visual review. Matching merges coincident tile
fragments within 1 meter, excludes named tracks from other lines, limits station
snaps to 300 meters and rejects excessive detours. Eram-e Sabz on line 4 has a
specific 400-meter allowance: its shared interchange coordinate is about 347
meters from the line 4 track. This does not move the station marker or relax
matching for other stations. Airport line 8 is treated as
the app's line 1 airport branch. Paths are inferred display geometry, not verified
railway signaling or a replacement for timetable connections.

The API network snapshot includes ordered segment coordinates. Restart the API
after building to serve the new contract. The web map and journey map use these
curves, reversing coordinates for reverse travel, with the existing straight-line
fallback for connections without geometry. Generated export and review files
can be removed after import; the app reads paths from the API and its cache.
