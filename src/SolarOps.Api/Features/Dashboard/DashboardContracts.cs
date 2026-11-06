namespace SolarOps.Api.Features.Dashboard;

/// <summary>Counts for the staff dashboard. A part is null when the caller's role may not read that data.</summary>
/// <param name="GeneratedAt">Time the summary was computed (UTC).</param>
/// <param name="Tickets">Service ticket counts; null without the TicketsRead policy.</param>
/// <param name="Quotes">Quote counts; null without the QuotesRead policy (e.g. Technician).</param>
public sealed record DashboardSummaryResponse(DateTimeOffset GeneratedAt, DashboardTicketsResponse? Tickets, DashboardQuotesResponse? Quotes);

/// <param name="Open">Tickets that are not Resolved or Closed.</param>
/// <param name="New">Tickets in state New (not triaged yet).</param>
/// <param name="ByPriority">Open tickets by priority.</param>
public sealed record DashboardTicketsResponse(int Open, int New, DashboardTicketPriorityResponse ByPriority);

public sealed record DashboardTicketPriorityResponse(int High, int Medium, int Low);

/// <param name="Sent">Quotes in status Sent.</param>
/// <param name="SentGrossTotal">Sum of the gross totals of the Sent quotes in EUR.</param>
/// <param name="Open">Quotes in status Draft or Sent.</param>
public sealed record DashboardQuotesResponse(int Sent, decimal SentGrossTotal, int Open);
