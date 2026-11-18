using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Rail.Api.Models;
using Rail.Api.Models.Metro;

if (args.Length < 1) throw new ArgumentException("Commands: migrate | export <file> | import <file>");
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Rail.Api"));
var configuration = new ConfigurationBuilder().SetBasePath(root)
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets<AppDbContextFactory>(optional: true)
    .AddEnvironmentVariables().Build();
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(configuration.GetConnectionString("DefaultConnection")).Options;
await using var db = new AppDbContext(options);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
switch (args[0])
{
    case "verify":
        var snapshot = await new Rail.Api.Controllers.NetworkController(db).LoadCachedData(default);
        Console.WriteLine($"Database: {snapshot.Segments!.Length} segments, {snapshot.Segments.Count(s => s.Coordinates.Length >= 2)} paths, {snapshot.Segments.Sum(s => s.Coordinates.Length)} ordered points.");
        break;
    case "migrate":
        await db.Database.MigrateAsync();
        Console.WriteLine("Database migrations applied.");
        break;
    case "export":
        var segments = await db.TrackSegments.AsNoTracking().OrderBy(s => s.Id).Select(s => new
        {
            s.Id, s.SourceId, s.RouteId, s.Route.LineId, s.FromStationId, s.ToStationId,
            from = new[] { s.FromStation.Longitude, s.FromStation.Latitude },
            to = new[] { s.ToStation.Longitude, s.ToStation.Latitude }
        }).ToArrayAsync();
        await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(segments, json));
        Console.WriteLine($"Exported {segments.Length} connections.");
        break;
    case "import":
        var paths = JsonSerializer.Deserialize<PathImport[]>(await File.ReadAllTextAsync(args[1]), json)
            ?? throw new InvalidDataException("Missing paths.");
        if (paths.Select(p => p.Id).Distinct().Count() != paths.Length) throw new InvalidDataException("Duplicate segment IDs.");
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var ids = paths.Select(p => p.Id).ToArray();
            var existing = await db.TrackSegments.Include(s => s.Route).Include(s => s.FromStation).Include(s => s.ToStation)
                .Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
            foreach (var path in paths)
            {
                if (!existing.TryGetValue(path.Id, out var segment) || segment.FromStationId != path.FromStationId || segment.ToStationId != path.ToStationId)
                    throw new InvalidDataException($"Segment identity changed: {path.Id}");
                if (path.Coordinates.Length < 2 || path.Coordinates.Any(p => p.Length != 2 || Math.Abs(p[0]) > 180 || Math.Abs(p[1]) > 90))
                    throw new InvalidDataException($"Invalid path: {path.Id}");
                if (Gap(path.Coordinates[0], segment.FromStation) > SnapLimit(segment, segment.FromStation)
                    || Gap(path.Coordinates[^1], segment.ToStation) > SnapLimit(segment, segment.ToStation))
                    throw new InvalidDataException($"Path endpoints do not match station direction: {path.Id}");
            }
            // Only matched segments are replaced; unmatched/manual geometry is preserved.
            await db.TrackSegmentPoints.Where(p => ids.Contains(p.TrackSegmentId)).ExecuteDeleteAsync();
            foreach (var path in paths)
                db.TrackSegmentPoints.AddRange(path.Coordinates.Select((p, index) => new TrackSegmentPoint
                { TrackSegmentId = path.Id, Sequence = index, Longitude = p[0], Latitude = p[1] }));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        Console.WriteLine($"Imported {paths.Length} paths / {paths.Sum(p => p.Coordinates.Length)} points.");
        break;
    default: throw new ArgumentException("Unknown command.");
}

static double Gap(decimal[] point, Station station)
{
    var dx = (double)(point[0] - station.Longitude) * 111320 * Math.Cos((double)station.Latitude * Math.PI / 180);
    var dy = (double)(point[1] - station.Latitude) * 111320;
    return Math.Sqrt(dx * dx + dy * dy);
}

static double SnapLimit(TrackSegment segment, Station station) =>
    segment.Route.LineId == 4 && station.Id == "eram-e-sabz" ? 401 : 301;

record PathImport(int Id, string FromStationId, string ToStationId, decimal[][] Coordinates);
