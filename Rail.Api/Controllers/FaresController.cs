using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Services;

namespace Rail.Api.Controllers;

[ApiController]
[Route("api/fares")]
public sealed class FaresController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<FareCatalog> Catalog([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var effectiveDate = date ?? DateOnly.FromDateTime(TehranTime.Now(clock));
        var products = await db.FareProducts.AsNoTracking().OrderBy(p => p.Code)
            .Select(p => new FareProductInfo(p.Code, p.NameFa, p.NameEn, p.DescriptionFa, p.DescriptionEn,
                p.Prices.Where(price => price.EffectiveFrom <= effectiveDate).OrderByDescending(price => price.EffectiveFrom)
                    .Select(price => new FarePriceInfo(price.EffectiveFrom, price.AmountRials, price.SourceUrl)).FirstOrDefault()))
            .ToArrayAsync(cancellationToken);
        return new FareCatalog(effectiveDate, "IRR", products);
    }

    [HttpGet("{code}/prices")]
    public async Task<ActionResult<FarePriceInfo[]>> History(
        string code,
        [FromQuery, Range(0, 100_000)] int offset = 0,
        [FromQuery, Range(1, 200)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var productId = await db.FareProducts.AsNoTracking().Where(p => p.Code == code)
            .Select(p => (int?)p.Id).SingleOrDefaultAsync(cancellationToken);
        if (productId is null)
            return NotFound();

        return await db.FarePrices.AsNoTracking().Where(p => p.FareProductId == productId)
            .OrderByDescending(p => p.EffectiveFrom).Skip(offset).Take(limit)
            .Select(p => new FarePriceInfo(p.EffectiveFrom, p.AmountRials, p.SourceUrl)).ToArrayAsync(cancellationToken);
    }

    // TODO : To quote a journey fare, obtain the approved dated fare rules for zones, airport/branch services,
    // media, transfer windows and concessions. Model rule precedence and validate against published examples.
    // For purchases or top-ups, select the payment and ticketing integration and define idempotency, refunds,
    // transaction reconciliation and which operator system is authoritative for card balances.
}

public sealed record FareCatalog(DateOnly Date, string Currency, FareProductInfo[] Products);
public sealed record FareProductInfo(string Code, string NameFa, string? NameEn, string? DescriptionFa, string? DescriptionEn,
    FarePriceInfo? Price);
public sealed record FarePriceInfo(DateOnly EffectiveFrom, long AmountRials, string? SourceUrl);
