using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Dashboard;

public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken ct);
}

/// <summary>Dashboard counts. Everything is aggregated in SQL; a part is left out when the role has no read policy for it.</summary>
internal sealed class DashboardService(AppDbContext db, ICustomerScope customerScope, ICurrentUser currentUser, TimeProvider clock)
    : IDashboardService
{
    public async Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var tickets = CanRead(AuthorizationPolicies.TicketsRead) ? await GetTicketsAsync(scope, ct) : null;
        var quotes = CanRead(AuthorizationPolicies.QuotesRead) ? await GetQuotesAsync(scope, ct) : null;
        return new DashboardSummaryResponse(clock.GetUtcNow(), tickets, quotes);
    }

    /// <summary>The roles of a read policy are the single source of truth (<see cref="AuthorizationPolicies.Matrix"/>).</summary>
    private bool CanRead(string policy) => AuthorizationPolicies.Matrix[policy].Any(currentUser.IsInRole);

    private async Task<DashboardTicketsResponse> GetTicketsAsync(DataScope scope, CancellationToken ct)
    {
        var query = db.Set<ServiceTicket>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            query = query.Where(x => x.CustomerId == own);
        }

        var open = await query
            .Where(x => x.State != ServiceTicketState.Resolved && x.State != ServiceTicketState.Closed)
            .GroupBy(x => new { x.Priority, IsNew = x.State == ServiceTicketState.New })
            .Select(g => new { g.Key.Priority, g.Key.IsNew, Count = g.Count() })
            .ToListAsync(ct);

        int Count(ServiceTicketPriority priority) => open.Where(x => x.Priority == priority).Sum(x => x.Count);

        return new DashboardTicketsResponse(
            open.Sum(x => x.Count),
            open.Where(x => x.IsNew).Sum(x => x.Count),
            new DashboardTicketPriorityResponse(
                Count(ServiceTicketPriority.High), Count(ServiceTicketPriority.Medium), Count(ServiceTicketPriority.Low)));
    }

    private async Task<DashboardQuotesResponse> GetQuotesAsync(DataScope scope, CancellationToken ct)
    {
        var query = db.Set<Quote>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            query = query.Where(x => x.CustomerId == own);
        }

        var open = await query
            .Where(x => x.Status == QuoteStatus.Draft || x.Status == QuoteStatus.Sent)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Gross = g.Sum(x => x.GrossTotal) })
            .ToListAsync(ct);

        var sent = open.FirstOrDefault(x => x.Status == QuoteStatus.Sent);
        return new DashboardQuotesResponse(sent?.Count ?? 0, sent?.Gross ?? 0m, open.Sum(x => x.Count));
    }
}
