# Navigation

`GET /api/navigation/walking-journeys` accepts an optional repeated `viaStationIds`
query parameter, for example `viaStationIds=station-a&viaStationIds=station-b`.
The default is empty. Select up to eight distinct operational stations. Both
"depart at" and "arrive by" searches require the rail journey to visit all selected
stations, including intermediate scheduled stops on the same train. The planner
chooses their order. Infeasible constraints return 404 instead of a direct walking
suggestion. Unknown or unavailable stations return 400.

Endpoint titles are resolved on the device from `tehran-region.pmtiles` using
the nearest named main city street (primary, secondary, or tertiary road),
followed by a Persian comma and the nearest named smaller street within 250 meters.
Duplicate names are omitted. Businesses, landmarks, highways, and ramps are excluded. The web and MAUI app share
the same archive reader as the basemap; MAUI reads packaged byte ranges through
the native bridge. The nearby metro title appears first and remains the fallback
when no suitable local label is available. No reverse-geocoding API or key is used.
These are location labels, rather than guaranteed complete postal addresses.
Neshan is still used for traffic-aware street routing.

Journey responses include `options`: the shortest available trip plus up to five
alternatives, ordered by `totalSeconds` ascending. Each option includes its street
access/exit, train legs, and departure/arrival times. Alternatives use distinct
boarding/alighting station pairs with the same travel modes, required stations,
and time constraints. Timetable and street lookups are reused. Total time includes
street travel, platform access, train travel, transfers, and waiting. Arrival-deadline
options use their actual departure and arrival times. `id` identifies the selected
option; the top-level fields default to the shortest option for older clients.
