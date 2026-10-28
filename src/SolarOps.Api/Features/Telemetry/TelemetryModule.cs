using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Telemetry;

/// <summary>Role sets of the Telemetry module.</summary>
public static class TelemetryRoles
{
    /// <summary>
    /// Interim access to the ingest until devices authenticate with API keys (#96); the simulator and gateways
    /// are not users. Replaced by the device key scheme in #96.
    /// </summary>
    public static readonly string[] Ingest = [Roles.Admin, Roles.Operator];
}

/// <summary>Telemetry ingest (#95): inverters (or the simulator) send batches of measurements.</summary>
public sealed class TelemetryModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITelemetryIngestService, TelemetryIngestService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/telemetry").WithTags("Telemetry");

        group.MapPost("/", Ingest)
            .WithName("IngestTelemetry")
            .WithSummary("Ingest a batch of inverter measurements")
            .WithDescription("Contract: ADR #185 (schemaVersion 1.0, 1 to 288 readings on the 5-minute UTC grid). The batch is atomic: " +
                "any invalid reading gives 400 (keys like readings[3].timestamp) and nothing is stored. Idempotent by (deviceId, timestamp): " +
                "already stored readings are ignored and counted in 'duplicates'. 404 for an unknown installation.")
            .WithValidation<TelemetryBatchRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(TelemetryRoles.Ingest));
    }

    internal static async Task<Accepted<TelemetryIngestResponse>> Ingest(
        TelemetryBatchRequest request, ITelemetryIngestService service, CancellationToken ct) =>
        TypedResults.Accepted((string?)null, await service.IngestAsync(request, ct));
}
