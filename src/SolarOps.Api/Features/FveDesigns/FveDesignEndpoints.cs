using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.FveDesigns;

internal static class FveDesignEndpoints
{
    internal static async Task<Created<FveDesignResponse>> Create(
        CreateFveDesignRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        CancellationToken ct)
    {
        // TODO(after merge with the Customers stack): verify that the customer and supply point exist.
        var design = FveDesign.Request(
            request.CustomerId!.Value,
            request.SupplyPointId!.Value,
            Normalize(request.Notes),
            currentUser.RequireUserId(),
            time.GetUtcNow());

        db.Add(design);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/designs/{design.Id}", FveDesignResponse.From(design));
    }

    internal static async Task<Ok<List<FveDesignResponse>>> List(
        AppDbContext db,
        CancellationToken ct,
        FveDesignStatus? status = null,
        Guid? customerId = null,
        Guid? supplyPointId = null)
    {
        var query = db.Set<FveDesign>().AsNoTracking();
        if (status is not null)
        {
            query = query.Where(d => d.Status == status);
        }

        if (customerId is not null)
        {
            query = query.Where(d => d.CustomerId == customerId);
        }

        if (supplyPointId is not null)
        {
            query = query.Where(d => d.SupplyPointId == supplyPointId);
        }

        var designs = await query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
        return TypedResults.Ok(designs.Select(FveDesignResponse.From).ToList());
    }

    internal static async Task<Ok<FveDesignResponse>> Get(Guid id, AppDbContext db, CancellationToken ct)
    {
        var design = await db.Set<FveDesign>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Design", id);
        return TypedResults.Ok(FveDesignResponse.From(design));
    }

    internal static async Task<Ok<FveDesignResponse>> RecordSiteSurvey(
        Guid id,
        SiteSurveyRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        CancellationToken ct)
    {
        var design = await LoadAsync(db, id, ct);
        design.RecordSiteSurvey(request.Notes!.Trim(), currentUser.RequireUserId(), time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FveDesignResponse.From(design));
    }

    internal static async Task<Ok<FveDesignResponse>> MarkInfeasible(
        Guid id,
        MarkInfeasibleRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var design = await LoadAsync(db, id, ct);
        design.MarkInfeasible(request.Reason!.Trim(), time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FveDesignResponse.From(design));
    }

    internal static async Task<Ok<FveDesignResponse>> UpdateParameters(
        Guid id,
        UpdateDesignParametersRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var design = await LoadAsync(db, id, ct);
        design.UpdateParameters(
            request.Latitude!.Value,
            request.Longitude!.Value,
            request.AnnualConsumptionKwh!.Value,
            request.ConsumptionProfile!.Value,
            request.ToRoofPlanes(),
            time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FveDesignResponse.From(design));
    }

    internal static async Task<Ok<FveDesignResponse>> Approve(
        Guid id,
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var design = await LoadAsync(db, id, ct);
        var latestVersion = await db.Set<FveDesignVersion>()
            .Where(v => v.DesignId == id)
            .MaxAsync(v => (int?)v.VersionNumber, ct);

        design.Approve(latestVersion, userId, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FveDesignResponse.From(design));
    }

    internal static async Task<Ok<FveDesignResponse>> Rework(
        Guid id,
        ReworkDesignRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var now = time.GetUtcNow();
        var design = await LoadAsync(db, id, ct);
        var wasApproved = design.Status == FveDesignStatus.Approved;

        design.Rework(request.Reason!.Trim(), userId, now);

        // Withdrawing an approval makes quotes built from it stale; flagged in the same SaveChanges as the design.
        if (wasApproved)
        {
            var quotes = await db.Set<Quote>()
                .Where(q => q.DesignId == id && (q.Status == QuoteStatus.Draft || q.Status == QuoteStatus.Sent))
                .ToListAsync(ct);
            foreach (var quote in quotes)
            {
                quote.MarkForRecalculation(now);
            }
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FveDesignResponse.From(design));
    }

    internal static async Task<Created<QuoteResponse>> CreateQuote(
        Guid id,
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var design = await LoadAsync(db, id, ct);
        if (design.Status != FveDesignStatus.Approved || design.ApprovedVersionNumber is not { } versionNumber)
        {
            throw new DomainException($"A quote can only be created from an approved design; the design is {design.Status}.");
        }

        var customer = await db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == design.CustomerId, ct)
            ?? throw new RequestValidationException(nameof(FveDesign.CustomerId), "Customer does not exist.");
        customer.EnsureActive();

        var version = await db.Set<FveDesignVersion>().AsNoTracking()
            .FirstAsync(v => v.DesignId == id && v.VersionNumber == versionNumber, ct);

        // Prices and names are the snapshot saved in the approved version, so the quote matches the design exactly.
        QuoteLineData[] lines =
        [
            new(version.PanelId, ComponentType.Panel, version.PanelCode, version.PanelName, version.PanelUnitPrice, version.PanelCount),
            new(version.InverterId, ComponentType.Inverter, version.InverterCode, version.InverterName, version.InverterUnitPrice, 1),
        ];

        var quote = Quote.Create(
            design.CustomerId, lines, Quote.DefaultVatRatePercent, null, userId, time.GetUtcNow(), design.Id, versionNumber);
        db.Add(quote);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/quotes/{quote.Id}", QuoteResponse.FromEntity(quote));
    }

    internal static async Task<FveDesign> LoadAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        await db.Set<FveDesign>().FirstOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new NotFoundException("Design", id);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
