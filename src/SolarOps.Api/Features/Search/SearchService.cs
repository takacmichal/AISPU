using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;
using SolarOps.Api.Infrastructure.Querying;

namespace SolarOps.Api.Features.Search;

public interface ISearchService
{
    Task<SearchResponse> SearchAsync(SearchQuery query, CancellationToken ct);
}

/// <summary>
/// Global search. Every group is a SQL-side ILike with <c>Take(limit)</c>; a group is skipped (null) when the role has no read
/// policy for it (<see cref="AuthorizationPolicies.Matrix"/>) and a customer account is limited to its own records by <see cref="ICustomerScope"/>.
/// </summary>
internal sealed class SearchService(AppDbContext db, ICustomerScope customerScope, ICurrentUser currentUser) : ISearchService
{
    private sealed record CustomerSearchData(Guid Id, string Email, CustomerType Type, string? FirstName, string? LastName, string? CompanyName)
    {
        public string DisplayName => Type == CustomerType.Company ? CompanyName ?? string.Empty : $"{FirstName} {LastName}".Trim();
    }

    private static readonly Expression<Func<Customer, CustomerSearchData>> CustomerProjection = c =>
        new(c.Id, c.Email, c.Type, c.FirstName, c.LastName, c.CompanyName);

    public async Task<SearchResponse> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var pattern = SqlLike.Contains(query.Term);
        var limit = query.Limit;

        var customers = CanRead(AuthorizationPolicies.CustomersRead) ? await FindCustomersAsync(pattern, limit, ct) : null;
        var quotes = CanRead(AuthorizationPolicies.QuotesRead) ? await FindQuotesAsync(pattern, limit, scope, ct) : null;
        var installations = CanRead(AuthorizationPolicies.InstallationsRead) ? await FindInstallationsAsync(pattern, limit, scope, ct) : null;
        var tickets = CanRead(AuthorizationPolicies.TicketsRead) ? await FindTicketsAsync(pattern, limit, scope, ct) : null;
        return new SearchResponse(customers, quotes, installations, tickets);
    }

    /// <summary>The roles of a read policy are the single source of truth (<see cref="AuthorizationPolicies.Matrix"/>).</summary>
    private bool CanRead(string policy) => AuthorizationPolicies.Matrix[policy].Any(currentUser.IsInRole);

    private async Task<IReadOnlyList<SearchHit>> FindCustomersAsync(string pattern, int limit, CancellationToken ct)
    {
        var customers = await db.Set<Customer>().AsNoTracking()
            .Where(CustomerSearch.Matches(pattern))
            .OrderBy(c => c.SortName).ThenBy(c => c.Id)
            .Take(limit)
            .Select(CustomerProjection)
            .ToListAsync(ct);
        return customers.ConvertAll(c => new SearchHit(c.Id, c.DisplayName, c.Email));
    }

    private async Task<IReadOnlyList<SearchHit>> FindQuotesAsync(string pattern, int limit, DataScope scope, CancellationToken ct)
    {
        var query = db.Set<Quote>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            query = query.Where(q => q.CustomerId == own);
        }

        var quotes = await query.Matching(db, pattern)
            .OrderByDescending(q => q.CreatedAt).ThenBy(q => q.Id)
            .Take(limit)
            .Select(q => new { q.Id, q.CustomerId, q.Status, q.GrossTotal })
            .ToListAsync(ct);
        var names = await CustomerNamesAsync(quotes.Select(q => q.CustomerId), ct);
        return quotes.ConvertAll(q => new SearchHit(
            q.Id,
            names.GetValueOrDefault(q.CustomerId, string.Empty),
            string.Create(CultureInfo.InvariantCulture, $"{q.Status} · {q.GrossTotal:0.00} EUR")));
    }

    private async Task<IReadOnlyList<SearchHit>> FindInstallationsAsync(string pattern, int limit, DataScope scope, CancellationToken ct)
    {
        var query = db.Set<Installation>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            query = query.Where(x => x.CustomerId == own);
        }

        var installations = await query.Matching(db, pattern)
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(limit)
            .Select(x => new { x.Id, x.Name, x.CustomerId, x.State })
            .ToListAsync(ct);
        var names = await CustomerNamesAsync(installations.Select(x => x.CustomerId), ct);
        return installations.ConvertAll(x => new SearchHit(x.Id, x.Name, Describe(x.State, names.GetValueOrDefault(x.CustomerId))));
    }

    private async Task<IReadOnlyList<SearchHit>> FindTicketsAsync(string pattern, int limit, DataScope scope, CancellationToken ct)
    {
        var query = db.Set<ServiceTicket>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            query = query.Where(x => x.CustomerId == own);
        }

        var customers = db.Set<Customer>().Where(CustomerSearch.NameMatches(pattern)).Select(c => c.Id);
        var tickets = await query
            .Where(x => EF.Functions.ILike(x.Title, pattern, SqlLike.EscapeCharacter) || customers.Contains(x.CustomerId))
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(limit)
            .Select(x => new { x.Id, x.Title, x.CustomerId, x.State })
            .ToListAsync(ct);
        var names = await CustomerNamesAsync(tickets.Select(x => x.CustomerId), ct);
        return tickets.ConvertAll(x => new SearchHit(x.Id, x.Title, Describe(x.State, names.GetValueOrDefault(x.CustomerId))));
    }

    private static string Describe(Enum state, string? customerName) =>
        string.IsNullOrEmpty(customerName) ? state.ToString() : $"{state} · {customerName}";

    /// <summary>Display names of the given customers in one query (at most a few dozen ids).</summary>
    private async Task<Dictionary<Guid, string>> CustomerNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        var customers = await db.Set<Customer>().AsNoTracking().Where(c => distinct.Contains(c.Id))
            .Select(CustomerProjection).ToListAsync(ct);
        return customers.ToDictionary(c => c.Id, c => c.DisplayName);
    }
}
