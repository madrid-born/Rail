# Rail Vue frontend

Vue 3, TypeScript, Vue Router, and Vite provide the Persian RTL interface for
MAUI. The installed app uses bundled HTML/CSS/JavaScript in HybridWebView;
the UI needs no web server, Node.js runtime, CDN, or remote font. The stations
page reads its network data from Rail.Api and requires access to that service.

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
src/navigation.ts           Tab names, paths, labels, and icons
src/router.ts               Route components and page titles
src/styles/theme.css        Shared theme and responsive styles
dist/                       Generated MAUI assets (ignored by Git)
```

Keep page components directly in `src/pages/` and reusable components in
`src/components/`. The layout is defined once in `App.vue`. Pages supply content only. Hash routing
(`#/map`, `#/stations`, `#/saved`, `#/settings`) changes pages without reloading
the document or requiring server-side route handling. Unknown routes return to
the map. Map and saved content remain placeholders. Stations are loaded from
the API and grouped by line and route.

## Browser development

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
account, and station-coordinate data.

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
