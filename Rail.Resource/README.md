# Shared resources and maintenance tools

| Location | Purpose |
| --- | --- |
| `Data` | Git-ignored PMTiles archive, GraphHopper JAR and regional routing PBF |
| `map-data` | Tracked map glyphs, sprites and supporting files |
| `Design` | Design source, prototypes and handoff documentation |
| `Logo` | Branding and platform icon assets |
| `Tools` | .NET database maintenance commands |
| `Scripts` | Railway extraction and path-generation scripts |

Run the database commands from the repository root:

```powershell
dotnet run --project Rail.Resource -- verify
```

See [railway maintenance](Tools/README.md) for migration, export, generation and
import commands. Before running the JavaScript extractor, install its packages:

```powershell
npm ci --prefix Rail.Resource
```

All tests, including script tests, live under `Rail.Tests`. The running app uses
database paths through the API and its cache; maintenance scripts are run manually.
