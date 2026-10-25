using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

    Task<QuoteResponse> SendAsync(Guid id, SendQuoteRequest? request, CancellationToken ct);

    Task<QuoteResponse> AcceptAsync(Guid id, QuoteNoteRequest? request, CancellationToken ct);

    Task<QuoteResponse> RejectAsync(Guid id, QuoteNoteRequest? request, CancellationToken ct);

    Task<IReadOnlyList<QuoteStateChangeResponse>> HistoryAsync(Guid id, CancellationToken ct);

    /// <summary>Marks every sent quote past its validity as Expired and returns how many were changed.</summary>
    Task<int> ExpireOverdueAsync(CancellationToken ct);
}

public sealed class QuoteService(AppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider, IOptions<QuoteOptions> options) : IQuoteService
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

    public async Task<QuoteResponse> SendAsync(Guid id, SendQuoteRequest? request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var quote = await FindForUpdateAsync(id, ct);
        quote.Send(request?.ValidityDays ?? options.Value.DefaultValidityDays, userId, request?.Note, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return QuoteResponse.FromEntity(quote);
    }

    public Task<QuoteResponse> AcceptAsync(Guid id, QuoteNoteRequest? request, CancellationToken ct) =>
        AnswerAsync(id, (quote, userId, now) => quote.Accept(userId, request?.Note, now), ct);

    public Task<QuoteResponse> RejectAsync(Guid id, QuoteNoteRequest? request, CancellationToken ct) =>
        AnswerAsync(id, (quote, userId, now) => quote.Reject(userId, request?.Note, now), ct);

    public async Task<IReadOnlyList<QuoteStateChangeResponse>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Set<Quote>().AnyAsync(q => q.Id == id, ct))
        {
            throw new NotFoundException("Quote", id);
        }

        var changes = await db.Set<QuoteStateChange>().AsNoTracking()
            .Where(c => c.QuoteId == id)
            .OrderBy(c => c.ChangedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);
        return changes.ConvertAll(QuoteStateChangeResponse.FromEntity);
    }

    public async Task<int> ExpireOverdueAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var overdue = await db.Set<Quote>()
            .Where(q => q.Status == QuoteStatus.Sent && q.ValidUntil != null && q.ValidUntil <= now)
            .ToListAsync(ct);
        foreach (var quote in overdue)
        {
            quote.Expire(now);
        }

        await db.SaveChangesAsync(ct);
        return overdue.Count;
    }

    // An answer to a quote past its validity is refused; the quote is marked Expired first so the state is not stale.
    private async Task<QuoteResponse> AnswerAsync(Guid id, Action<Quote, Guid, DateTimeOffset> answer, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow();
        var quote = await FindForUpdateAsync(id, ct);
        if (quote.IsOverdue(now))
        {
            quote.Expire(now);
            await db.SaveChangesAsync(ct);
            throw new DomainException("The quote has expired.");
        }

        answer(quote, userId, now);
        await db.SaveChangesAsync(ct);
        return QuoteResponse.FromEntity(quote);
    }

    private async Task<Quote> FindForUpdateAsync(Guid id, CancellationToken ct) =>
        await db.Set<Quote>().Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, ct)
        ?? throw new NotFoundException("Quote", id);
}
