# Rail

<img src="Rail.Statics/Logo/03-PNG/Rail-App-Icon-128.png" width="80" alt="Rail’s Persian Teal icon combines an R, Azadi Tower, and metro tracks">

Rail is a Persian-first companion for Tehran Metro riders. It brings journey planning, station information, scheduled departures, and service notices into one place.

The Windows app currently runs the interactive design prototype with sample data. The API runs separately against SQL Server and uses the imported metro network and timetables. The [implementation handoff](Rail.Statics/Design/04-handoff/implementation-notes.md) maps the prototype screens to the API.

## What Rail does

- Plans journeys by earliest scheduled arrival and shows boarding stations, train directions, transfers, and arrival times in Tehran local time.
- Provides Persian and English station search, nearby stations, line and branch information, amenities, and scheduled departure boards.
- Exposes trip stops, service calendars, published alerts, and fare catalog entries when those records are available.
- Supports accounts and saved stations through ASP.NET Core Identity. The prototype simulates the sign-in and favorites flows locally.

## Design preview

![Rail’s Windows journey results showing departure, arrival, transfer count, and a schematic metro route](Rail.Statics/Design/02-screens/desktop/journeys-1440.png)

![Rail mobile designs for the network map, station search, station details, departures, and scheduled trip stops](Rail.Statics/Design/05-overview/02-network-and-departures.png)

Open the [clickable prototype](Rail.Statics/Design/START-HERE.html) in Edge or Chrome, or browse the [screen gallery](Rail.Statics/Design/01-prototype/gallery.html). Start with the sample trip from Tajrish to Farhangsara via Imam Khomeini. The maps are schematic and the preview times and alerts are illustrative.

## Solution structure

| Project | Role | Where to start |
| --- | --- | --- |
| [Rail.Api](Rail.Api/Rail.Api.csproj) | ASP.NET Core Web API on .NET 10 with EF Core, SQL Server, and Identity | [Controllers](Rail.Api/Controllers), [models](Rail.Api/Models), and [journey planner](Rail.Api/Services/JourneyPlanner.cs) |
| [Rail.Maui](Rail.Maui/Rail.Maui.csproj) | Windows-only .NET MAUI desktop app with Rail branding and an embedded offline prototype | [App startup](Rail.Maui/App.xaml.cs), [prototype host](Rail.Maui/MainPage.xaml), and [theme colors](Rail.Maui/Resources/Styles/Colors.xaml) |
| [Rail.Statics](Rail.Statics/Rail.Statics.csproj) | Design and branding source files shared by the browser preview and MAUI app | [Design pack](Rail.Statics/Design/README.md) and [logo kit](Rail.Statics/Logo/README.md) |

MAUI bundles the prototype’s HTML, CSS, JavaScript, and logo assets directly from `Rail.Statics/Design`. Edit the shared prototype there and rebuild the app to pick up the changes.

## Run locally

### Windows app

Use Windows with the .NET 10 SDK, the .NET MAUI Windows workload, and the WebView2 runtime.

```powershell
dotnet build Rail.Maui/Rail.Maui.csproj
```

In Visual Studio, set `Rail.Maui` as the startup project and select **Windows Machine**. The prototype opens after the splash. The grid button in the app header opens the 20-screen catalog. The preview works without running the API or SQL Server.

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

- [Design system and components](Rail.Statics/Design/03-design-system/components.html)
- [MAUI and API implementation handoff](Rail.Statics/Design/04-handoff/implementation-notes.md)
- [Brand and platform asset guide](Rail.Statics/Logo/README.md)
