# Metro data

`Rail` is the API database. The existing `Metro` LocalDB database is the navigation data source. The first EF Core migration creates the `Metro` schema for application data and the `Identity` schema for accounts in `Rail`.

From the repository root, apply the migration and import into the empty local `Rail` database.

```powershell
dotnet ef database update --project Rail.Api/Rail.Api.csproj --context AppDbContext
sqlcmd -S '(localdb)\MSSQLLocalDB' -d Rail -E -C -b -i Rail.Api/Data/ImportLegacyMetro.sql
```

The first command uses the configured `ConnectionStrings:LocalDB` and requires the `dotnet-ef` tool. The import requires both databases on the same SQL Server instance. It reads the source in place, copies the network and timetable in a single transaction, and rejects a target that already has network data.

## Model conventions

- Stations have stable IDs and Persian and English names. Station membership, ordered route stops, and track segments describe different parts of the network. Track segments can be traversed in either direction. Transfer rules are directional and can apply to a pair of lines or narrow the transfer to specific routes.
- Schedule groups define a route and service pattern. Trips retain their advertised direction and actual origin and destination. Stop times are local wall-clock times with a day offset from the Tehran service date. Imported boarding stops use the scheduled arrival as the departure, matching the old departures view. The last stop at the destination has no departure.
- `GetServiceDayTypeCodeAsync` checks the calendar for the requested Tehran date and otherwise uses the Saturday–Wednesday, Thursday, or Friday timetable. Official holidays in the source use the Friday timetable. Calendar dates use `DateOnly` and can be formatted in the Jalali calendar for display.
- Station amenities record availability and verification separately. A missing amenity row means the status is unknown. Entrances can have their own locations and step-free access information.
- An alert becomes eligible for display once published and applies during its time window. An alert with no line or station targets applies across the network. Fare prices are stored as whole Iranian rials. Use the latest effective price on or before the requested Tehran date. Favorite stations are tied to Identity accounts and are deleted with the account.

The import retains stable navigation IDs and removes duplicate line and route fields in the new schema. Legacy source metadata, trip source ordinals, and formatted calendar strings remain available in the original database. Entrances, alerts, and fares start empty.
