using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Rail.Api.Services;

public sealed record StreetStep(string Instruction, double DistanceMeters, int Seconds);
public sealed record StreetPath(string Mode, double DistanceMeters, int Seconds, double[][] Coordinates,
    StreetStep[] Steps, string Source, string TrafficModel = "none");

public sealed class StreetRouting(HttpClient http, IConfiguration configuration, IMemoryCache cache)
{
    public async Task<StreetPath> RouteAsync(double fromLat, double fromLng, double toLat, double toLng,
        string mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var defaultProfile = mode switch
        {
            "walk" => "foot", "car" => "car", "motorcycle" => "motorcycle",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var profile = configuration[$"GraphHopper:Profiles:{mode}"] ?? defaultProfile;
        var key = (nameof(StreetRouting), http.BaseAddress, profile, fromLat, fromLng, toLat, toLng, mode);
        if (cache.TryGetValue<StreetPath>(key, out var saved)) return saved!;
        var distance = WalkingJourney.Distance(fromLat, fromLng, toLat, toLng);
        if (distance < 1) return new(mode, 0, 0, [[fromLng, fromLat], [toLng, toLat]], [], "graphhopper");
        var speed = mode switch { "car" => 8.33, "motorcycle" => 6.94, _ => WalkingJourney.MetersPerSecond };
        var fallback = new StreetPath(mode, Math.Round(distance), (int)Math.Ceiling(distance / speed), [], [], "estimated");
        if (http.BaseAddress is null || string.IsNullOrWhiteSpace(profile)) return fallback;
        try
        {
            // Requests use latitude,longitude; unencoded GeoJSON responses use longitude,latitude.
            // GraphHopper's time fields are milliseconds, while Rail's contract uses seconds.
            var query = FormattableString.Invariant($"route?point={fromLat},{fromLng}&point={toLat},{toLng}")
                + $"&profile={Uri.EscapeDataString(profile)}&locale=fa&points_encoded=false&instructions=true&calc_points=true&elevation=false";
            using var response = await http.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.BadRequest) return fallback;
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!response.IsSuccessStatusCode)
                return IsUnreachable(json.RootElement) ? fallback with { Source = "no_route" } : fallback;
            var paths = json.RootElement.GetProperty("paths");
            if (paths.GetArrayLength() == 0) return fallback with { Source = "no_route" };
            var path = paths[0];
            var meters = NonNegative(path.GetProperty("distance"));
            var seconds = Seconds(path.GetProperty("time"));
            var points = path.GetProperty("points");
            if (points.GetProperty("type").GetString() != "LineString") return fallback;
            var coordinates = points.GetProperty("coordinates").EnumerateArray().Select(point =>
            {
                if (point.GetArrayLength() < 2) throw new FormatException("Invalid route coordinate.");
                var longitude = point[0].GetDouble();
                var latitude = point[1].GetDouble();
                if (!double.IsFinite(longitude) || !double.IsFinite(latitude) || Math.Abs(longitude) > 180 || Math.Abs(latitude) > 90)
                    throw new FormatException("Invalid route coordinate.");
                return new[] { longitude, latitude };
            }).ToArray();
            if (coordinates.Length < 2) return fallback;
            var steps = path.TryGetProperty("instructions", out var instructions)
                ? instructions.EnumerateArray().Select(step => new StreetStep(
                    step.GetProperty("text").GetString() ?? "", NonNegative(step.GetProperty("distance")), Seconds(step.GetProperty("time")))).ToArray()
                : [];
            var result = new StreetPath(mode, meters, seconds, coordinates, steps, "graphhopper");
            cache.Set(key, result, TimeSpan.FromMinutes(5));
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return fallback; }
        catch (HttpRequestException) { return fallback; }
        catch (JsonException) { return fallback; }
        catch (InvalidOperationException) { return fallback; }
        catch (KeyNotFoundException) { return fallback; }
        catch (FormatException) { return fallback; }
    }

    private static double NonNegative(JsonElement value)
    {
        var number = value.GetDouble();
        if (!double.IsFinite(number) || number < 0) throw new FormatException("Invalid route measurement.");
        return number;
    }

    private static int Seconds(JsonElement value)
    {
        var seconds = Math.Ceiling(NonNegative(value) / 1000);
        if (seconds > int.MaxValue) throw new FormatException("Route duration is too large.");
        return (int)seconds;
    }

    private static bool IsUnreachable(JsonElement root) => root.TryGetProperty("hints", out var hints)
        && hints.ValueKind == JsonValueKind.Array
        && hints.EnumerateArray().Any(hint => hint.TryGetProperty("details", out var details)
            && details.GetString() is "com.graphhopper.util.exceptions.ConnectionNotFoundException"
                or "com.graphhopper.util.exceptions.PointNotFoundException"
                or "com.graphhopper.util.exceptions.PointOutOfBoundsException");
}
