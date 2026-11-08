using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;

namespace Rail.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/favorite-stations")]
public sealed class FavoritesController(AppDbContext db, UserManager<ApplicationUser> users, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FavoriteStationInfo[]>> List(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        return await db.FavoriteStations.AsNoTracking().Where(f => f.UserId == user.Id)
            .OrderByDescending(f => f.AddedAtUtc).ThenBy(f => f.StationId)
            .Select(f => new FavoriteStationInfo(f.StationId, f.Station.NameFa, f.Station.NameEn, f.AddedAtUtc))
            .ToArrayAsync(cancellationToken);
    }

    [HttpPut("{stationId}")]
    public async Task<IActionResult> Add(string stationId, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var addedAt = clock.GetUtcNow();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Metro].[FavoriteStations] ([UserId], [StationId], [AddedAtUtc])
            SELECT u.[Id], s.[Id], {addedAt}
            FROM [Identity].[AspNetUsers] AS u WITH (HOLDLOCK)
            CROSS JOIN [Metro].[Stations] AS s WITH (HOLDLOCK)
            WHERE u.[Id] = {user.Id} AND s.[Id] = {stationId}
              AND NOT EXISTS (
                  SELECT 1 FROM [Metro].[FavoriteStations] AS f WITH (UPDLOCK, HOLDLOCK)
                  WHERE f.[UserId] = u.[Id] AND f.[StationId] = s.[Id]
              )
            """, cancellationToken);
        if (inserted > 0)
            return NoContent();

        if (!await db.Users.AnyAsync(u => u.Id == user.Id, cancellationToken))
            return Unauthorized();

        return await db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpDelete("{stationId}")]
    public async Task<IActionResult> Remove(string stationId, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        await db.FavoriteStations.Where(f => f.UserId == user.Id && f.StationId == stationId)
            .ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record FavoriteStationInfo(string StationId, string NameFa, string NameEn, DateTimeOffset AddedAtUtc);
