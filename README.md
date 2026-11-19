# Rail

<img src="Rail.Resource/Logo/03-PNG/Rail-App-Icon-128.png" width="80" alt="Rail’s Persian Teal icon combines an R, Azadi Tower, and metro tracks">

Rail is a Persian-first companion for Tehran Metro riders. It brings journey planning, station information, scheduled departures, and service notices into one place.

The MAUI app hosts a local Vue frontend with four pages: map, stations, saved items, and settings. Shared components provide a responsive Persian RTL shell with glass-style bottom navigation. Stations persist locally, grouped by line with ordered routes and interchange labels. On startup/resume, missing data or data older than 24 hours refreshes silently from Rail.Api. Failed refreshes preserve the old copy and show a warning only when that copy is over 24 hours old. The map uses MapLibre with a bundled offline Protomaps regional archive, Persian labels, and metro overlays. Saved content remains a placeholder, and appearance settings work locally. The API runs separately against SQL Server and uses the imported metro network and timetables. The [implementation handoff](Rail.Resource/Design/04-handoff/implementation-notes.md) maps the design prototype screens to the API.

## What Rail does

- Plans journeys by earliest scheduled arrival and shows boarding stations, train directions, transfers, and arrival times in Tehran local time.
- Provides Persian and English station search, nearby stations, line and branch information, amenities, and scheduled departure boards.
- Exposes trip stops, service calendars, published alerts, and fare catalog entries when those records are available.
- Supports email/password and Google accounts through ASP.NET Core Identity. Settings shows the signed-in account; MAUI securely retains refresh tokens across app updates. Saved-item UI remains a placeholder.

Map files, fonts and icons live in `Rail.Resource/map-data`; MAUI bundles them for offline Android/Windows map display, while Vite serves local browser previews independently of the API; Nginx can serve the same assets for a standalone browser deployment on Ubuntu x64. See the [map hosting guide](Rail.Resource/map-data/README.md). The basemap needs no provider key; street routing uses self-hosted GraphHopper. See the [routing setup guide](infrastructure/graphhopper/README.md).

## Design preview

![Rail’s Windows journey results showing departure, arrival, transfer count, and a schematic metro route](Rail.Resource/Design/02-screens/desktop/journeys-1440.png)

![Rail mobile designs for the network map, station search, station details, departures, and scheduled trip stops](Rail.Resource/Design/05-overview/02-network-and-departures.png)

Open the [clickable prototype](Rail.Resource/Design/START-HERE.html) in Edge or Chrome, or browse the [screen gallery](Rail.Resource/Design/01-prototype/gallery.html). Start with the sample trip from Tajrish to Farhangsara via Imam Khomeini. The maps are schematic and the preview times and alerts are illustrative.

## Solution structure

| Project | Role | Where to start |
| --- | --- | --- |
| [Rail.Api](Rail.Api/Rail.Api.csproj) | ASP.NET Core Web API on .NET 10 with EF Core, SQL Server, and Identity | [Controllers](Rail.Api/Controllers), [models](Rail.Api/Models), and [journey planner](Rail.Api/Services/JourneyPlanner.cs) |
| [Rail.Maui](Rail.Maui/Rail.Maui.csproj) | Windows and Android .NET MAUI app with Rail branding and embedded offline pages | [App startup](Rail.Maui/App.xaml.cs), [HTML host](Rail.Maui/MainPage.xaml), and [theme colors](Rail.Maui/Resources/Styles/Colors.xaml) |
| [Rail.WebPages](Rail.WebPages/Rail.WebPages.csproj) | Vue/TypeScript frontend bundled locally by MAUI | [Components, pages, and development](Rail.WebPages/README.md) |
| [Rail.Resource](Rail.Resource/Rail.Resource.csproj) | Shared map/design assets and database/path maintenance tools | [Resources](Rail.Resource/README.md), [design pack](Rail.Resource/Design/README.md) and [logo kit](Rail.Resource/Logo/README.md) |
| [Rail.Tests](Rail.Tests/Rail.Tests.csproj) | Solution tests grouped by the project they cover | [Test commands](Rail.Tests/README.md) |

MAUI builds the Vue source in `Rail.WebPages/src` with Vite and bundles the generated HTML, CSS, and JavaScript from `Rail.WebPages/dist`. Edit the Vue components and rebuild the app to pick up changes. The original design prototype remains available separately in `Rail.Resource/Design`.

## Run locally

### Windows app

Use Windows with the .NET 10 SDK, the .NET MAUI Windows workload, the WebView2 runtime, and Node.js 22.12+ with npm on PATH. The MAUI build restores frontend dependencies as needed and builds Vue automatically.

```powershell
dotnet build Rail.Maui/Rail.Maui.csproj -p:EnableAndroidTarget=false
```

In Visual Studio, set `Rail.Maui` as the startup project and select **Windows Machine**. The map page opens after the splash. Use the bottom navigation to switch between the four pages. The UI, regional map and an initial metro network snapshot are bundled locally, so the map works on a fresh offline install. Rail.Api and its database provide network refreshes and online features. Downloaded data remains usable if the API is unavailable. Start the API with its `https` profile for the default local connection. For browser development, run `npm ci` and `npm run dev` inside `Rail.WebPages`; see the [frontend guide](Rail.WebPages/README.md) for proxy, cache, and deployed API configuration.

For live Vue updates inside the Windows app, keep `npm run dev` running and launch
MAUI in Debug with the **Windows Machine (Vue Dev)** profile. Save a component to
update both browser and app without rebuilding MAUI. The normal profile and all
Release builds continue to use bundled offline assets.

### Android app

Install the Android SDK and Java SDK. On Windows, the Android target appears when the project finds the SDK in Visual Studio's default location or through `ANDROID_HOME`. Set `ANDROID_HOME` to your SDK location and restart Visual Studio if you use a custom location. Then select an Android emulator or connected device for `Rail.Maui`.

```powershell
dotnet build Rail.Maui/Rail.Maui.csproj -f net10.0-android -p:EnableAndroidTarget=true
```

### API and database

The API reads `ConnectionStrings:LocalDB` and uses the `Rail` database by default. To point it at another local database, set `ConnectionStrings__LocalDB` in the launch environment.

For a fresh local database, apply the EF Core migration from the repository root. If the original `Metro` database is available on the same LocalDB instance, run the import once to populate the empty network tables. The migration requires `dotnet-ef`, and the import command uses `sqlcmd`.

```powershell
dotnet ef database update --project Rail.Api/Rail.Api.csproj --context AppDbContext
sqlcmd -S '(localdb)\MSSQLLocalDB' -d Rail -E -C -b -i Rail.Api/Data/ImportLegacyMetro.sql
```

See the [database guide](Rail.Api/Data/README.md) for import behavior and timetable conventions.

```powershell
dotnet run --project Rail.Api/Rail.Api.csproj --launch-profile https
```

Open [Swagger UI](https://localhost:7130/swagger) in Development or try the examples in [Rail.Api.http](Rail.Api/Rail.Api.http). A quick read-only check is `GET /api/network/lines`. Account email flows require SMTP settings under `Email`. Supply the SMTP password through local configuration or the `Email__Password` environment variable.

## Further reading

- [TimeTableCache: holiday-aware active trips and scheduled positions](Rail.Api/TIMETABLE-CACHE.md)

- [Google login setup and API client contract](Rail.Api/GOOGLE-LOGIN.md)
- [Design system and components](Rail.Resource/Design/03-design-system/components.html)
- [MAUI and API implementation handoff](Rail.Resource/Design/04-handoff/implementation-notes.md)
- [Brand and platform asset guide](Rail.Resource/Logo/README.md)
