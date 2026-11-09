# Rail implementation handoff

## Build order

1. Build the Windows shell, journey form, Persian and English station search, result and journey steps. Keep guest access for all public queries.
2. Add station details, departure filtering, trip stops and a reviewable schematic. Prefer real native controls and allow a station list as an alternative to tapping a map.
3. Add notices, fare catalog and service calendar. Preserve distinct states for no data, an expired cache and a failed request.
4. Add sign-in, token storage and favorite stations. Introduce offline persistence only when freshness, sync and retry rules have been defined.

## Screens and data contracts

| ID | Screen | Endpoint or implementation |
| --- | --- | --- |
| 01–02 | Planner and picker | `GET /api/stations?q=&lineId=&offset=&limit=`. Keep `fromStationId` and `toStationId` as stable IDs and validate a distinct pair. |
| 03–05 | Result, steps and no journey | `GET /api/navigation/journeys` with station IDs, `date`, `after`, optional `searchHours` and `fallbackTransferMinutes`. `legs: null` is a valid no-result response. |
| 06 | Network | `GET /api/network/lines`, `/routes/{routeId}`, `/segments`, and `/transfer-rules`. Use branch route IDs and reviewed positions for a production schematic. |
| 07–08 | Stations and details | `GET /api/stations`, `/api/stations/{stationId}` and optional `/api/stations/nearby`. Coordinates are for proximity, and entrance accessibility is separate from a verified step-free route. |
| 09–10 | Departures and trip | `GET /api/stations/{stationId}/departures?date=&after=&lineId=&directionStationId=` and `GET /api/timetable/trips/{tripId}`. Show day offsets and the direction and destination separately when they differ. |
| 11–12 | Alerts and detail | `GET /api/service-alerts` with optional line, station, `at` and `includeUpcoming`, then `GET /api/service-alerts/{alertId}`. Label active and upcoming using the published window. |
| 13 | Fares | `GET /api/fares?date=` and `GET /api/fares/{code}/prices`. Show IRR, effective date and source. The current API serves a catalog and price history. |
| 14 | Service calendar | `GET /api/service-calendar?date=` and `GET /api/service-calendar/overrides`. Display Jalali dates in the Persian UI while keeping ISO Gregorian `DateOnly` at the API boundary. A null holiday flag means unverified. |
| 15–16 | Favorites and sign-in | `GET /api/me/favorite-stations` and `PUT` / `DELETE /api/me/favorite-stations/{stationId}`. Use `/api/authentication/login` and `/refresh`; preserve the action that led to sign-in. |
| 17 | Settings | Store presentation preferences locally. A complete English interface needs localized strings and an LTR layout audit. |
| 18–20 | Offline, request error and empty | Cache with version and source freshness. Keep form inputs through retry. Distinguish 400, 401, 404, transport failures and valid empty results. |

## Interaction details

- In a journey, use the server's Tehran local timestamps, `serviceDate` and day offsets. Avoid converting wall-clock timetable values to the rider's device zone. For each leg show departure station, arrival station, line, route, direction and train type where relevant. Mark estimated transfer time using `usedFallback` and confidence, including source estimates.
- Show the full time and date around midnight. Cancel stale requests when a date or station filter changes. Keep network failures distinct from a successful search with no journey.
- Use a keyboard-accessible station list alongside the diagram. Give line colors visible numbers. Use `FlowDirection="RightToLeft"` for Persian views and isolate English names and times in LTR spans. Test screen-reader reading order, Windows keyboard focus, high contrast and text scaling.
- On wide Windows screens, put navigation in a right-side rail. Keep the planner and network diagram side by side. Use a vertical flow on narrower screens and allow normal scrolling at larger text sizes.
- Keep auth tokens in secure platform storage and retain only non-sensitive UI preferences outside it. After an auth expiry, show the account gate and preserve the station the rider tried to save.
- Keep service alerts, missing amenity verification, and scheduled departures distinct. The design does not imply real-time data, verified station access or disruption-aware routing.

## Items that need a production decision

- Validate all schedule, map and station data against an authoritative source. Decide how official geometry and attribution are handled and how accessibility and closures are verified.
- Decide whether offline trips can be calculated locally. The preview only simulates the cached-map and retry states. Define cache expiry, versioning, storage footprint and conflict behavior first.
- Define full account registration, confirmation, recovery, and deletion screens around the existing Identity flows.
- Plan error localization, empty and loading states, and platform tests alongside the Windows implementation. The exported desktop frames are browser design references.

The prototype uses existing station IDs and line colors with made-up times. All UI state resets on reload.

