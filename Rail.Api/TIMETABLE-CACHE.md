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
