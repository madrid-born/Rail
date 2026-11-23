using Rail.Api.Models.Metro;

namespace Rail.Api.Services;

// Provisional citywide assumptions, not measured/live traffic. Times are Tehran local.
public sealed class TrafficEstimate(Func<DateOnly, string> dayType)
{
    private static readonly double[] Hours = [0, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24];
    private static readonly double[] Weekday = [1, 1.3, 3.5, 1.8, 1.7, 2.5, 3.5, 5, 3, 1.5, 1];
    private static readonly double[] Thursday = [1, 1.2, 2.3, 1.6, 2, 2.8, 3.2, 3.8, 3.2, 2, 1];
    private static readonly double[] Holiday = [1, 1, 1.1, 1.3, 1.6, 1.8, 2, 2.5, 2.3, 1.7, 1];

    public double Coefficient(DateTime at, string mode) => Value(at.TimeOfDay.TotalHours, DateOnly.FromDateTime(at), mode);

    private double Value(double hour, DateOnly date, string mode)
    {
        if (mode == "walk") return 1;
        var values = dayType(date) switch
        {
            ServiceDayType.Thursday => Thursday,
            ServiceDayType.FridayOrHoliday => Holiday,
            _ => Weekday
        };
        var index = Math.Clamp(Array.FindLastIndex(Hours, h => h <= hour), 0, Hours.Length - 2);
        var coefficient = values[index] + (values[index + 1] - values[index])
            * (hour - Hours[index]) / (Hours[index + 1] - Hours[index]);
        // A car peak of 5 becomes 2 for motorcycles; no coefficient drops below 1.
        return mode == "motorcycle" ? 1 + .25 * (coefficient - 1) : coefficient;
    }

    // Consume baseline travel at rate 1/coefficient per real second. For each linear
    // interval the integral is analytic, so forward/backward calculations are inverses.
    public double Seconds(double baselineSeconds, DateTime at, string mode, bool arriveBy = false)
    {
        if (!double.IsFinite(baselineSeconds) || baselineSeconds < 0) throw new ArgumentOutOfRangeException(nameof(baselineSeconds));
        if (mode is not ("walk" or "car" or "motorcycle")) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == "walk" || baselineSeconds == 0) return baselineSeconds;
        var elapsed = 0d;
        var remaining = baselineSeconds;
        while (remaining > 1e-8)
        {
            var date = DateOnly.FromDateTime(arriveBy && at.TimeOfDay == TimeSpan.Zero ? at.AddTicks(-1) : at);
            var midnight = date.ToDateTime(TimeOnly.MinValue);
            var hour = (at - midnight).TotalHours;
            var boundaryHour = arriveBy ? Hours.Last(h => h < hour) : Hours.First(h => h > hour);
            var boundary = midnight.AddHours(boundaryHour);
            var span = Math.Abs((boundary - at).TotalSeconds);
            var initial = Value(hour, date, mode);
            var final = Value(boundaryHour, date, mode);
            var slope = (final - initial) / span;
            var capacity = Math.Abs(slope) < 1e-12 ? span / initial : Math.Log(final / initial) / slope;
            if (remaining <= capacity + 1e-8)
                return elapsed + (Math.Abs(slope) < 1e-12 ? remaining * initial
                    : initial * (Math.Exp(slope * remaining) - 1) / slope);
            remaining -= capacity;
            elapsed += span;
            at = boundary;
        }
        return elapsed;
    }

    public StreetPath Apply(StreetPath path, DateTime at, bool arriveBy = false)
    {
        if (path.Mode == "walk" || path.Source == "no_route") return path;
        var duration = Seconds(path.Seconds, at, path.Mode, arriveBy);
        var departure = arriveBy ? at.AddSeconds(-duration) : at;
        var totalStepSeconds = path.Steps.Sum(s => (double)s.Seconds);
        var baseline = 0d;
        var previous = 0;
        var steps = path.Steps.Select(step =>
        {
            baseline += totalStepSeconds > 0 ? step.Seconds * path.Seconds / totalStepSeconds : 0;
            var cumulative = (int)Math.Ceiling(Seconds(baseline, departure, path.Mode) - 1e-7);
            var result = step with { Seconds = cumulative - previous };
            previous = cumulative;
            return result;
        }).ToArray();
        return path with { Seconds = (int)Math.Ceiling(duration - 1e-7), Steps = steps,
            TrafficModel = "assumed_day_hour_v1", BaselineSeconds = path.Seconds };
    }
}
