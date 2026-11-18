# Solution tests

All automated tests live here, grouped by the project they cover:

| Folder | Coverage |
| --- | --- |
| `Rail.Api` | API integration, network data, authentication, routing and timetables |
| `Rail.Maui` | Packaged map archive reading and caching |
| `Rail.WebPages` | Frontend services and local map hosting |
| `Rail.Resource` | Railway path matching scripts |

From the repository root:

```powershell
# .NET API and MAUI tests
dotnet test Rail.Tests/Rail.Tests.csproj

# Install frontend dependencies (also needed by the Vite integration test)
npm ci --prefix Rail.WebPages
npm ci --prefix Rail.Tests
npm test --prefix Rail.Tests

# Python path-matching tests (standard library only)
python -B Rail.Tests/Rail.Resource/test_track_paths.py
```

On Windows accounts without Event Log write permission, set
`Logging__EventLog__LogLevel__Default=None` for the test process.
