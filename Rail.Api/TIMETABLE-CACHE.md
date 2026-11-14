# TimeTableCache

`GET /api/timetable/TimeTableCache?dateTime=2026-11-11T10:11:00`

Omit `dateTime` (or leave it empty) to use now in Tehran. An ISO timestamp without
an offset is interpreted in `Asia/Tehran`, independent of the server's timezone.
UTC (`Z`) and explicit offsets are converted to Tehran before selecting the date.
For query strings, encode a literal plus sign as `%2B`, for example
`2026-11-11T10:11:00%2B03:30`. Invalid input returns 400.

## Service day

The endpoint checks `https://holidayapi.ir/gregorian/{year}/{month}/{day}` for the
Tehran Gregorian date. The provider's top-level `is_holiday` boolean takes
priority; event descriptions are not used for classification.

| Holiday flag / weekday | Timetable code |
| --- | --- |
| Holiday, on any weekday | `friday` |
| Non-holiday Friday | `friday` |
| Non-holiday Thursday | `thursday` |
| Other non-holiday weekdays | `saturday_wednesday` |

This method uses that classification directly; existing stored service-calendar
overrides and other timetable/departure endpoints are unchanged. Successful
holiday decisions are cached on the API for 24 hours per Gregorian date. Failed,
invalid, or timed-out holiday responses are not cached and return 503 rather
than silently assuming an ordinary weekday. The upstream timeout is 10 seconds.

## Trips and positions

The response is a plain array of trips, without pagination or metadata:

```json
[
  {
    "id": 105,
    "routeId": "line-1-main",
    "lineId": 1,
    "directionStationId": "tajrish",
    "position": {
      "station": null,
      "fromStation": "mirdamad",
      "toStation": "doctor-shariati"
    }
  }
]
```

Only active trips with a resolved position are returned: departure is on or before
the requested instant, and arrival is still later. Future departures are omitted,
rather than returned with an all-null position. A trip finishing exactly at the
requested instant is already over and is excluded. Missing or inconsistent stop
times also exclude a trip instead of inventing a position.
Overnight journeys from previous service dates remain included until they finish.
Each such occurrence uses its own service date's holiday classification, which
can differ from today's. Service dates are used internally, not returned.
Departure/arrival day offsets are respected, including offsets greater than one.
Templates whose actual departure falls on a later civil day are not included.

Each trip contains only `id`, `routeId`, `lineId`, `directionStationId`, and
`position`. Position fields are nullable station-ID strings, with no nested names:

- At a station: `station` holds its ID; `fromStation` and `toStation` are null.
- Between stations: `station` is null; `fromStation` and `toStation` hold IDs.
- Unstarted or unresolved trips are not returned; no trip has three null fields.

Stops are processed in trip sequence, so reverse directions and express trips use
their actual scheduled stop order. An exact scheduled arrival is at the station,
including a zero-dwell stop; at a later departure boundary, the trip is between
stops. Completed terminal arrivals are excluded before calculating position.

These are timetable estimates, not GPS positions, delay observations, or
confirmation of actual operation. The response has `Cache-Control: no-store`:
positions are specific to the requested instant, and must not use the station
page's 24-hour local cache policy. The app requests the exact current minute
(`:00.000`) for its minute-based display, so zero-dwell station arrivals can be
shown at their scheduled minute. Explicit second/millisecond inputs retain their
precision in the API; after a zero-dwell arrival, the trip is between stops.

Provider reference: [Holiday API documentation](https://holidayapi.ir/).

## Station detail board

The initial station detail page uses one aggregate request:

`GET /api/stations/{stationId}/page?lineId=3`

The response contains `station` (including facilities and entrances),
`selectedLineId`, available `lines`, full `routes` through the station on those
lines, their `stations`, the initial train `board`, and `boardUnavailable`.
`lineId` is optional: a saved line is used if it still serves this station;
otherwise the lowest line number is selected. Unknown stations return 404.
No separate station, network, account, or initial train-board request is made
when opening this public page, including a cold deep link. Opening a train's
journey uses the stop times already present in the response.

Station metadata and timetable headers are cached on the server for up to one
minute. Train positions and next arrivals are recalculated for every request;
they are not frozen in that cache. The board first queries trip headers and
only the selected station's stop, then fetches full journeys in a batch for
active trains and the next train in each direction. Malformed journeys are
discarded and the next valid candidate is checked.

Subsequent line changes, resume, and 30-second refreshes use the lighter endpoint:

`GET /api/timetable/stations/{stationId}/board?lineId=3`

The station detail page uses this narrower endpoint. Optional `dateTime` has the
same Tehran/ISO semantics as `TimeTableCache`. A missing station/line association
returns 404; invalid line numbers or dates return 400. Responses use `no-store`.

The response includes `stationId`, `lineId`, `requestedAt`, `until`,
`nextArrivals`, and `trips`. The forecast covers 24 hours from `requestedAt`.
`nextArrivals` contains the earliest scheduled arrival for each route/direction,
including a train currently dwelling at the selected station. Inactive station,
line membership, or route-stop status prevents boarding suggestions. Trains
that skip the station or have already departed it cannot be its next arrival.

`trips` contains trains with a known current position on routes serving the
station, plus any unstarted train needed for a next-arrival suggestion. Each
trip includes its `serviceDate` (combine with `id` for a unique occurrence),
route/line/direction IDs, `position`, and every scheduled stop in travel order.
Each stop has `sequence`, `stationId`, `arrivalAt`, and nullable `departureAt`.
All times include their date and Tehran offset, including overnight services.
Future trains have a null position. Express trains include only their scheduled
stops, allowing the UI to mark other route stations as skipped.

The page refreshes every 30 seconds while visible and on resume. Both the train
position and arrival countdowns refer to `requestedAt`; stale data retains that
timestamp and displays an update failure. These remain timetable estimates,
without live train tracking or observed delays.
