using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Rail.Api.Services;

public sealed record StreetStep(string Instruction, double DistanceMeters, int Seconds);
public sealed record StreetPath(string Mode, double DistanceMeters, int Seconds, double[][] Coordinates,
    StreetStep[] Steps, string Source);

public sealed class StreetRouting(HttpClient http, IConfiguration configuration, IMemoryCache cache)
{
    public async Task<StreetPath> RouteAsync(double fromLat, double fromLng, double toLat, double toLng,
        string mode, CancellationToken cancellationToken)
    {
        var key = (nameof(StreetRouting), fromLat, fromLng, toLat, toLng, mode);
        if (cache.TryGetValue<StreetPath>(key, out var saved)) return saved!;
        var distance = WalkingJourney.Distance(fromLat, fromLng, toLat, toLng);
        if (distance < 1) return new(mode, 0, 0, [[fromLng, fromLat], [toLng, toLat]], [], "neshan");
        var speed = mode switch { "car" => 8.33, "motorcycle" => 6.94, _ => WalkingJourney.MetersPerSecond };
        var fallback = new StreetPath(mode, Math.Round(distance), (int)Math.Ceiling(distance / speed), [], [], "estimated");
        var apiKey = configuration["Neshan:RoutingApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return fallback;
        try
        {
            var type = mode == "walk" ? "pedestrian" : mode;
            // The no-traffic endpoint ignores pedestrian/motorcycle types and returns
            // car routes. Use it only for cars; walking must use the mode-aware API.
            var endpoint = mode == "car" ? "v4/direction/no-traffic" : "v4/direction";
            var query = FormattableString.Invariant($"{endpoint}?type={type}&origin={fromLat},{fromLng}&destination={toLat},{toLng}&alternative=false");
            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Add("Api-Key", apiKey);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return fallback;
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var routes = json.RootElement.GetProperty("routes");
            if (routes.GetArrayLength() == 0) return fallback with { Source = "no_route" };
            var route = routes[0];
            var coordinates = new List<double[]>();
            var steps = new List<StreetStep>();
            double meters = 0;
            var seconds = 0;
            foreach (var leg in route.GetProperty("legs").EnumerateArray())
            {
                meters += leg.GetProperty("distance").GetProperty("value").GetDouble();
                seconds += (int)Math.Ceiling(leg.GetProperty("duration").GetProperty("value").GetDouble());
                foreach (var step in leg.GetProperty("steps").EnumerateArray())
                {
                    steps.Add(new(step.GetProperty("instruction").GetString() ?? "",
                        step.GetProperty("distance").GetProperty("value").GetDouble(),
                        (int)Math.Ceiling(step.GetProperty("duration").GetProperty("value").GetDouble())));
                    if (step.TryGetProperty("polyline", out var polyline)) coordinates.AddRange(DecodePolyline(polyline.GetString() ?? ""));
                }
            }
            if (coordinates.Count < 2 && route.TryGetProperty("overview_polyline", out var overview)
                && overview.TryGetProperty("points", out var points)) coordinates.AddRange(DecodePolyline(points.GetString() ?? ""));
            if (coordinates.Count < 2) return fallback;
            var result = new StreetPath(mode, meters, seconds, coordinates.ToArray(), steps.ToArray(), "neshan");
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

    public static double[][] DecodePolyline(string encoded)
    {
        var result = new List<double[]>();
        var index = 0;
        var latitude = 0;
        var longitude = 0;
        int Next()
        {
            var value = 0;
            var shift = 0;
            int digit;
            do
            {
                if (index >= encoded.Length || shift > 30) throw new FormatException("Invalid polyline");
                digit = encoded[index++] - 63;
                if (digit < 0 || digit > 63) throw new FormatException("Invalid polyline");
                value |= (digit & 31) << shift;
                shift += 5;
            } while (digit >= 32);
            return (value & 1) != 0 ? ~(value >> 1) : value >> 1;
        }
        while (index < encoded.Length)
        {
            latitude += Next(); longitude += Next();
            if (Math.Abs(latitude / 1e5) > 90 || Math.Abs(longitude / 1e5) > 180) throw new FormatException("Invalid coordinate");
            result.Add([longitude / 1e5, latitude / 1e5]);
        }
        return result.ToArray();
    }
}
