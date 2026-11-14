# Rail Vue frontend

Vue 3, TypeScript, Vue Router, and Vite provide the Persian RTL interface for
MAUI. The installed app uses bundled HTML/CSS/JavaScript in HybridWebView;
the UI needs no web server or Node.js runtime. The map downloads Neshan styles,
tiles and map fonts; the rest of the UI uses bundled fonts and assets. Network
and station data come from Rail.Api.

## Structure

```text
index.html                  Single HTML document
src/App.vue                 Shared header, main content outlet, and navigation
src/components/             Header, bottom navigation, icons, heading, empty state
src/pages/MapPage.vue        Map (landing page)
src/pages/StationsPage.vue   Stations
src/pages/SavedPage.vue      Saved items
src/pages/SettingsPage.vue   Settings
src/composables/useTheme.ts Shared appearance state and local persistence
src/services/network.ts     Single-call network download and persistent local cache
src/services/timetable.ts   Startup snapshot and minute-aligned timetable polling
src/services/trainPositions.ts Route/direction matching for train markers
src/navigation.ts           Tab names, paths, labels, and icons
src/router.ts               Route components and page titles
src/styles/theme.css        Shared theme and responsive styles
dist/                       Generated MAUI assets (ignored by Git)
```

Keep page components directly in `src/pages/` and reusable components in
`src/components/`. The layout is defined once in `App.vue`. Pages supply content only. Hash routing
(`#/map`, `#/stations`, `#/saved`, `#/settings`) changes pages without reloading
the document or requiring server-side route handling. Unknown routes return to
the map. Saved content remains a placeholder. Stations are loaded from
the API and grouped by line and route.

## Accounts and persistent sign-in

Settings shows a sign-in button when signed out and the account's display name
(or username/email) when signed in. `#/login` and `#/register` use email/password
and offer Google underneath; `#/recover` supports emailed password-reset codes.
Email registrations must confirm their email before signing in. SMTP must be configured.

`src/services/auth.ts` shares account state throughout the app. Use
`authenticatedFetch` for protected API calls: it adds the access token and renews
it automatically, sharing concurrent refreshes and retrying an unauthorized request once.
Startup, resume, and a ten-minute foreground check restore/renew the account.
Offline failures keep the cached account name; server authorization always requires
a valid token. A rejected refresh clears the saved account.

Bundled MAUI uses `AuthenticationBridge` and the OS `SecureStorage` for its refresh
token and cached profile; access tokens stay in memory. Its stable storage key and
application ID preserve sign-in through normal in-place app updates. Browser and
Vue Dev mode use origin-scoped localStorage for the refresh token/profile instead;
they have separate sessions from the bundled app. Never store passwords.
Clearing app/browser data, uninstalling, OS storage loss, account security changes,
or 180 days without a successful refresh can require signing in again. Online
logout invalidates all account refresh tokens; offline logout clears this device.

Google opens the system browser in MAUI, then exchanges a short-lived handoff by
polling the API. The browser preview uses a separate tab. Configure credentials
and the HTTPS callback as described in `../Rail.Api/GOOGLE-LOGIN.md`. Keep the
deployed API's data-protection keys persistent across redeployments and replicas;
its application discriminator is `Rail.Api`. Losing the keys invalidates sessions.

## Browser development

### Neshan map

Map point selection opens `/trip/new?from=longitude,latitude&to=longitude,latitude`.
The trip page calls `GET /api/navigation/walking-journeys` with both coordinates.
Optional query parameters are `dateTime` (ISO, interpreted in Tehran when no offset
is supplied), `timeMode=departAt|arriveBy`, `accessMode=walk|car|motorcycle`,
and `exitMode=walk|car|motorcycle`. Transfers use a fixed two minutes; this is not
a query/form setting. The form saves its choices in the URL; automatic refresh
preserves the selected time. Its Jalali calendar supports month/day selection,
manual Persian/Arabic/Latin date digits, and leap-date validation. Dates convert
to the API's ISO Gregorian representation internally; all visible dates are Jalali.
Date/time controls and the two travel-mode controls each share one row. The
inline radio choices are «شروع در» and «پایان در». Origin/destination buttons show
the nearest station to each selected point; each opens a map picker for only
that endpoint, preserving the other endpoint and draft planning choices. Confirm
applies the selected point; cancel retains the original point.
Departure searches minimize door-to-door arrival. Arrival searches work backward
to find the latest departure that includes the final street leg before the deadline.
BFS and DFS from both ends exclude disconnected candidates. Candidate stations
include the four nearest and the two nearest on each operational line, subject
to a 30-minute access budget. Direct walking is also compared.

Set the API's `Neshan:RoutingApiKey` using .NET user secrets in development or
`Neshan__RoutingApiKey` in the production environment. This service key stays on
the server and is separate from the browser map key. Street routes use Neshan's
mode-aware `/v4/direction` endpoint for walking/motorcycle geometry, durations,
and instructions, and `/v4/direction/no-traffic` for cars only (the no-traffic
endpoint silently returns car routes for pedestrian requests);
successful results are cached for five minutes. Requests run with bounded
concurrency. Walking is dashed and car/motorcycle paths are orange. Missing
street routes have a visible warning and are not replaced with invented geometry.
When the provider is unavailable, duration estimates use a 1.3 detour factor and
mode-dependent speed. Add 120 seconds per end for platform access/exit.
Rail sections remain station-to-station diagrams because the database does not
contain surveyed track/corridor polylines. All travel times are estimates;
car estimates exclude traffic, and future traffic is not predicted. Train positions and arrival countdowns use
database schedules, not observed train locations. Visible trip pages refresh
every 30 seconds and retain the last successful result with a warning on failure.

Set `VITE_NESHAN_MAP_KEY=web.your-map-key` in `.env.local` before running Vite
or building MAUI. This public browser key is bundled in the client; configure
its allowed origins in the Neshan dashboard (development and the MAUI WebView
origin must be permitted). The supplied instance key is configured locally and
is not committed. Restart Vite after changing environment values.

The map uses Neshan's MapLibre SDK, bundled through npm, and downloads styles,
fonts and tiles from Neshan at runtime. The basemap requires internet access and
a WebGL-capable WebView. Neshan/OpenStreetMap attribution remains visible.
Station markers and colored connections come from the shared database snapshot,
without per-station requests. Connections follow consecutive route stops,
keeping branches separate, and show direct geographic connections rather than
surveyed rail geometry. Missing coordinates break connections. Tap a station
marker or its label to select that point; the selection card also links to station details. The filter icon opens the
same map-preferences dialog available in Settings. Unchecking a line removes its
tracks and exclusive station markers; shared stations keep their full interchange
colors, including filtered-out lines. All lines may be hidden. Station-name visibility and transparent bordered
label boxes are independent options; the box option appears when names are enabled.
Names retain their zoom threshold and collision handling.

Preferences use the stable `rail.map.preferences` key, separately from network
caches and session-only camera state. Browser/Vue Dev saves to origin-scoped
localStorage. Bundled MAUI also saves to native `Preferences.Default` through the
existing bridge, so normal in-place updates preserve these choices even if the
WebView copy is lost. The newest timestamped copy repairs the other store, and
native writes are serialized to preserve rapid changes. Keep the application ID
and preference key stable across releases. Clearing app/browser data, uninstalling,
or changing browser origin may remove or separate saved preferences; settings
are device-local, not account-synced.
The station page's back arrow returns through app history, restoring the map's
camera or the station list's scroll position, search and expanded lines. A direct
station link with no prior app page falls back to Stations. View state is kept
for the current app session.
Cache storage version 2 refreshes older snapshots lacking coordinates; the API's
schema version remains 1 because coordinate fields are additive.

Install Node.js 22.12+ (or 20.19+), then run from this directory:

```powershell
npm ci
npm run dev
```

Open the local URL printed by Vite. For a production preview, use `npm run build`
then `npm run preview`. The development/preview server is only for browser work;
MAUI loads the bundled production output. Opening the source HTML with `file://`
is not supported because it uses JavaScript modules.

`npm run typecheck` checks TypeScript and Vue templates. `npm run build` runs
that check and generates production assets with relative URLs in `dist/`.

## Station data and API connection

Run Rail.Api with its `https` launch profile (https://localhost:7130). Browser
development and the MAUI Vue Dev profile use Vite's `/api` proxy to that address.
The proxy accepts the local self-signed development certificate; the frontend
does not disable TLS certificate validation. Restart Vite after changing its
proxy target.

At app startup, when no usable local snapshot exists, the client makes one request to
`GET /api/network/load-cached-data` (**Load Cached Data**). The versioned snapshot
contains all lines, all station summaries (no pagination), all routes and ordered
stops, and interchange memberships/statuses. The API performs three read-only
projections rather than a query per line or route, and omits unrelated timetable,
and account data. Station latitude/longitude are included for the map.

The complete snapshot is saved in WebView `localStorage`, under a versioned key
scoped to the configured API URL, and reused in memory. At startup and when the
app becomes visible again, the app checks the local save time. A snapshot no more
than 24 hours old requires **zero API calls**. An older snapshot refreshes silently
with one request, even if the Stations page is not open. Expanding lines and
switching tabs never trigger requests. A future timestamp is also rechecked to
handle device clock changes.

Existing data stays visible during a refresh. The app downloads and validates the
entire replacement before atomically replacing the localStorage entry; it never
clears the old snapshot first. Failed downloads or invalid responses preserve
both the saved data and its original timestamp. A successful refresh is silent,
with no refresh button, timestamp, cache paragraph, or success message.

If the cache is older than 24 hours and the refresh fails, an app-level modal
explains that the server is unavailable and the saved information may be inaccurate
or outdated. It appears regardless of the current tab and can be dismissed; it
does not repeat on tab changes. With no usable data and no connection, the Stations
page shows an unavailable state with a retry action instead of a stale-data warning.

Downloaded/stored snapshots are checked for schema compatibility and required
references. Missing, corrupt, or incompatible caches trigger a new download.
Concurrent startup/resume checks share one request, which completes and saves even
if the user switches tabs. If local storage is blocked or full, the app keeps a
complete session copy and leaves the previous disk copy intact. Browser development,
MAUI Vue Dev, and the bundled MAUI origin have separate origin-scoped storage.

Stops are sorted by `sequence`, not station name. Main and branch routes stay
separate. Station IDs join summaries to stops; other `lines` memberships provide
interchange labels, including inactive connections. Non-operational stops are
labeled rather than hidden.

Bundled MAUI builds use `https://localhost:7130` by default for Windows development.
For another API, set `VITE_API_BASE_URL` before building, or put it in `.env.local`:

```dotenv
VITE_API_BASE_URL=https://your-rail-api.example.com
```

Use the API origin, without `/api`. See `.env.example`. This public build-time URL
is bundled in the frontend. On Android or another device, localhost refers to that
device: use a reachable API address with a trusted HTTPS certificate.

Rail.Api permits read-only CORS requests from the bundled MAUI WebView origins
(`https://appdir`, with compatibility origins for other hosts). Override the
allowlist using the `Cors:AllowedOrigins` configuration array for a different
host. Restart Rail.Api after changing code/configuration. Production browser
hosting needs its origin in that allowlist too. Vite's same-origin proxy does not
need CORS. Loading errors appear with retry controls; no sample data is substituted.

## Timetable cache and route tracks

Every app launch requests `GET /api/timetable/TimeTableCache` with `dateTime` set
to the current minute's start, in UTC (converted to Tehran by the API). This
matches the timetable's minute precision, including zero-dwell arrivals. Its
validated response replaces the separate
`rail.timetable.v1:` localStorage snapshot and the shared in-memory copy. Network
metadata retains its independent 24-hour policy.

While the Stations page is visible, requests start at the next wall-clock minute
boundary (seconds zero), then each subsequent minute. The timer is re-aligned
after every tick rather than waiting a minute after each response. Leaving the
page stops polling; an already-started request may finish saving its response.
Hiding the app pauses polling and cancels pending work. Returning to the app
requests fresh positions and re-aligns polling when the Stations page is active.
Opening Stations later in a different minute requests fresh data immediately.
Browser/OS scheduling can delay timers; hidden WebViews do not keep polling.

Requests time out after 45 seconds. If a request spans a minute boundary, the new
minute supersedes it; the older response cannot overwrite newer data. Failed or
invalid responses preserve the previous disk/session snapshot. A small failure
message identifies when positions could not be updated; there are no update
buttons or success notifications. This cache does not use the daily data popup.

Each route has two plain black tracks, one on each side of its colored station
line. Only the middle line has station circles, slightly enlarged to 19px.
The left half turns white for a train moving along the left track's direction;
the right half turns white for the opposite direction. Both halves can be occupied
at once. Trains between stations appear as small arrows on the black tracks,
pointing in their direction of travel. Moving
trains use their `fromStation`/`toStation` order to choose a track; at-station trains
use `directionStationId`. Between-stop arrows sit at the midpoint of the two rendered stations, since the API provides
no distance/progress fraction. Express segments can span multiple stops. Branches
are matched by both line and route; the API returns only active, locatable trains.
Track geometry is measured from the actual station markers, including wrapped
station names and transfer badges, and updates when an accordion opens or resizes.
These positions are timetable estimates rather than live telemetry.

Run `npm test` for direction and route-matching checks.

## MAUI build

### Live Vue editing in the Windows app

1. Run `npm run dev` in `Rail.WebPages` and leave it running.
2. Start `Rail.Maui` in **Debug** using the **Windows Machine (Vue Dev)** launch
   profile. In Rider, select this launch profile in the run configuration.
3. Save a Vue component or stylesheet. Vite updates the running app, just as it
   updates the browser. Restart the app once when switching launch profiles.

From the repository root, you can also launch that profile with:

```powershell
dotnet run --project Rail.Maui/Rail.Maui.csproj -f net10.0-windows10.0.19041.0 --launch-profile "Windows Machine (Vue Dev)" -p:EnableAndroidTarget=false
```

This profile sets `RAIL_WEB_DEV_SERVER=http://127.0.0.1:5173`. Debug builds use
a regular WebView pointed at that URL so Vite's scripts and HMR WebSocket share
the same origin. Vite must be running before opening this profile. The Vite port
is fixed: if 5173 is occupied, Vite reports an error rather than choosing another
port. This profile is for Windows; it does not configure Android device networking.

The normal **Windows Machine** profile uses the bundled HybridWebView and needs
a rebuild to pick up Vue changes. Release builds always use bundled assets and
ignore `RAIL_WEB_DEV_SERVER`. The development WebView is intended for frontend
editing; use the normal profile when testing HybridWebView-specific .NET interop.

### Bundled offline build

Build `Rail.Maui` normally. Its build targets run `npm ci` when the dependency
manifests change or dependencies are missing, then `npm run build`, and include
the resulting `dist/` files under `rail-pages/`. Node.js/npm must be on PATH on
the development machine; the first dependency restore needs registry access.
Subsequent builds reuse installed packages. No manual copying is needed.

`MainPage.xaml` uses `HybridRoot="rail-pages"` and `DefaultFile="index.html"`.
The `.csproj` keeps the frontend visible in the .NET solution; Vue is compiled
by Vite, not the C# compiler. Building `Rail.WebPages.csproj` alone only builds
the .NET project; use `npm run build` to build its frontend independently.

The original design prototype remains in `Rail.Statics/Design/01-prototype`.
