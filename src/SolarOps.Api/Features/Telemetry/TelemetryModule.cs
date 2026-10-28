using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Features.DeviceKeys;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Telemetry;

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
                "already stored readings are ignored and counted in 'duplicates'. Authentication: installation API key in the X-Api-Key header (#96); " +
                "missing, unknown or revoked key = 401, installationId different from the key's installation = 403. 404 for an unknown installation.")
            .WithValidation<TelemetryBatchRequest>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(DeviceApiKeyDefaults.PolicyName);
    }

    internal static async Task<Results<Accepted<TelemetryIngestResponse>, ProblemHttpResult>> Ingest(
        TelemetryBatchRequest request, ClaimsPrincipal device, ITelemetryIngestService service, CancellationToken ct)
    {
        // A key is valid for its own installation only.
        if (device.FindFirstValue(DeviceApiKeyDefaults.InstallationClaim) != request.InstallationId?.ToString())
        {
            return TypedResults.Problem(
                title: "Forbidden",
                detail: "The API key does not belong to the installation in the request.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return TypedResults.Accepted((string?)null, await service.IngestAsync(request, ct));
    }
}
