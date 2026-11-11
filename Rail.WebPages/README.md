# Rail Vue frontend

Vue 3, TypeScript, Vue Router, and Vite provide the Persian RTL interface for
MAUI. The installed app uses bundled HTML/CSS/JavaScript in HybridWebView;
it needs no web server, Node.js runtime, API, CDN, or remote font.

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
src/navigation.ts           Tab names, paths, labels, and icons
src/router.ts               Route components and page titles
src/styles/theme.css        Shared theme and responsive styles
dist/                       Generated MAUI assets (ignored by Git)
```

Keep page components directly in `src/pages/` and reusable components in
`src/components/`. The layout is defined once in `App.vue`. Pages supply content only. Hash routing
(`#/map`, `#/stations`, `#/saved`, `#/settings`) changes pages without reloading
the document or requiring server-side route handling. Unknown routes return to
the map. Map, stations, and saved content remain placeholders.

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
