using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;

namespace SolarOps.Api.Features.Dashboard;

/// <summary>Counts for the staff dashboard (tickets and quotes), aggregated in the database.</summary>
public sealed class DashboardModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDashboardService, DashboardService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dashboard").WithTags("Dashboard");

        group.MapGet("/summary", GetSummary)
            .WithName("GetDashboardSummary")
            .WithSummary("Counts of tickets and quotes for the dashboard")
            .WithDescription("tickets: open (not Resolved or Closed), new (state New) and open by priority. " +
                "quotes: sent (status Sent), sentGrossTotal (sum of their gross totals, EUR) and open (Draft or Sent); " +
                "null for roles without access to quotes (Technician). A customer gets 403.")
            .RequireAuthorization(AuthorizationPolicies.DashboardRead);
    }

    internal static async Task<Ok<DashboardSummaryResponse>> GetSummary(IDashboardService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetSummaryAsync(ct));
}
