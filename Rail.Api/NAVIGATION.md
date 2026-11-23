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
Street routes use self-hosted GraphHopper for walking, car and motorcycle travel.
See [setup and deployment](../infrastructure/graphhopper/README.md). Raw GraphHopper
results are cached before adjustment. Car/motorcycle responses use
`trafficModel: "assumed_day_hour_v1"` and retain `baselineSeconds`; walking uses
`trafficModel: "none"`. This is a provisional citywide model, not measured or live traffic.
There is no seven-day car restriction.

Journey responses include `options`: the shortest available trip plus up to five
alternatives, ordered by `totalSeconds` ascending. Each option includes its street
access/exit, train legs, and departure/arrival times. Alternatives use distinct
boarding/alighting station pairs with the same travel modes, required stations,
and time constraints. Timetable and street lookups are reused. Total time includes
street travel, platform access, train travel, transfers, and waiting. Arrival-deadline
options use their actual departure and arrival times. `id` identifies the selected
option; the top-level fields default to the shortest option for older clients.

## Assumed street traffic

`TrafficEstimate` linearly interpolates car coefficients at Tehran-local hours
`0,6,8,10,12,14,16,18,20,22,24`:

| Day | Coefficients in hour order |
| --- | --- |
| Saturday-Wednesday | 1,1.3,3.5,1.8,1.7,2.5,3.5,5,3,1.5,1 |
| Thursday | 1,1.2,2.3,1.6,2,2.8,3.2,3.8,3.2,2,1 |
| Friday/holiday | 1,1,1.1,1.3,1.6,1.8,2,2.5,2.3,1.7,1 |

Motorcycle coefficients are `1 + 0.25 * (carCoefficient - 1)`, bounded 1-2.
Walking is unchanged; no per-kilometre stopping penalty is added. Travel consumes
baseline seconds at rate `1/coefficient` per real second. Integration is analytic
within each linear interval and advances the real clock, including across midnight.
Backward integration provides departure times for arrival deadlines. Platform time
is kept separate: after access driving and before exit driving. Instruction durations
are adjusted chronologically without mutating the cached baseline path.

Explicit database service-calendar overrides take precedence; otherwise the holiday
service determines the day type. Lookup failure returns 503 instead of assuming a
weekday. The same calendar is passed into the train planner for vehicle searches.
Traffic-adjusted access times determine train eligibility; exit times are evaluated
at each train arrival. Reverse searches evaluate exit at the deadline and access
backward from boarding. The existing per-end access budget applies after adjustment.

This changes station/train alternatives and door-to-door ranking. It does not change
GraphHopper's street geometry or avoid a particular congested road: that requires
road-level traffic data. The citywide model preserves earliest-arrival ordering for
a fixed street leg, which the planner relies on when keeping the best station labels.

Station entry and exit each add 300 seconds, outside traffic adjustment.
Line transfers remain 120 seconds. These times participate in train eligibility
and complete journey duration.
