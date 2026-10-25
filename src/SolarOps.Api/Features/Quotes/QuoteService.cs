using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Quotes;

public interface IQuoteService
{
    Task<QuoteResponse> CreateAsync(CreateQuoteRequest request, CancellationToken ct);

    Task<QuoteResponse> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<QuoteResponse>> ListAsync(Guid? customerId, QuoteStatus? status, CancellationToken ct);
}

public sealed class QuoteService(AppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider) : IQuoteService
{
    public async Task<QuoteResponse> CreateAsync(CreateQuoteRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var customerId = request.CustomerId!.Value;

        var customer = await db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new RequestValidationException(nameof(CreateQuoteRequest.CustomerId), "Customer does not exist.");
        customer.EnsureActive();

        var requested = request.Lines!;
        var componentIds = requested.Select(l => l.ComponentId!.Value).Distinct().ToList();
        var components = await db.Set<CatalogComponent>().AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var lines = new List<QuoteLineData>(requested.Count);
        for (var i = 0; i < requested.Count; i++)
        {
            if (!components.TryGetValue(requested[i].ComponentId!.Value, out var component))
            {
                throw new RequestValidationException($"Lines[{i}].ComponentId", "Component does not exist.");
            }

            component.EnsureActive();
            lines.Add(new QuoteLineData(component.Id, component.Type, component.Code, component.Name, component.UnitPrice, requested[i].Quantity!.Value));
        }

        var quote = Quote.Create(
            customerId, lines, request.VatRatePercent ?? Quote.DefaultVatRatePercent, request.Note, userId, timeProvider.GetUtcNow());
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync(ct);
        return QuoteResponse.FromEntity(quote);
    }

    public async Task<QuoteResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var quote = await db.Set<Quote>().AsNoTracking().Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, ct)
            ?? throw new NotFoundException("Quote", id);
        return QuoteResponse.FromEntity(quote);
    }

    public async Task<IReadOnlyList<QuoteResponse>> ListAsync(Guid? customerId, QuoteStatus? status, CancellationToken ct)
    {
        var query = db.Set<Quote>().AsNoTracking().Include(q => q.Lines).AsQueryable();
        if (customerId is { } c)
        {
            query = query.Where(q => q.CustomerId == c);
        }

        if (status is { } s)
        {
            query = query.Where(q => q.Status == s);
        }

        var quotes = await query.OrderByDescending(q => q.CreatedAt).ThenBy(q => q.Id).ToListAsync(ct);
        return quotes.ConvertAll(QuoteResponse.FromEntity);
    }
}
