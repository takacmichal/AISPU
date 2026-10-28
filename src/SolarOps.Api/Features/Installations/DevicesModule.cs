using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Installations;

/// <summary>Panels and inverters of an installation with serial numbers and replacement history (#83).</summary>
public sealed class DevicesModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDeviceService, DeviceService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/installations/{installationId:guid}/devices").WithTags("Devices");

        group.MapGet("/", List)
            .WithName("ListInstallationDevices")
            .WithSummary("List devices of an installation")
            .WithDescription("By default only installed devices; includeReplaced=true adds replaced ones. Filter: type (Panel, Inverter).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapGet("/{deviceId:guid}", Get)
            .WithName("GetInstallationDevice")
            .WithSummary("Get a device")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapGet("/{deviceId:guid}/history", History)
            .WithName("GetInstallationDeviceHistory")
            .WithSummary("Replacement history of a device")
            .WithDescription("The whole chain of devices the given one belongs to (replaced predecessors and successors), oldest first.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapPost("/", Add)
            .WithName("AddInstallationDevice")
            .WithSummary("Register a panel or inverter with its serial number")
            .WithDescription("The serial number is unique across all installations (case-insensitive), otherwise 409. Not possible after the installation has been handed over.")
            .WithValidation<AddDeviceRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsField);

        group.MapPost("/{deviceId:guid}/replace", Replace)
            .WithName("ReplaceInstallationDevice")
            .WithSummary("Replace an installed device with a new one, keeping the old one in history")
            .WithDescription("The new device has the same type. The replaced device stays with status Replaced. 409 when the device has been replaced already or the new serial number is taken.")
            .WithValidation<ReplaceDeviceRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsField);
    }

    internal static async Task<Ok<IReadOnlyList<DeviceResponse>>> List(
        Guid installationId, IDeviceService service, CancellationToken ct, string? type = null, bool includeReplaced = false) =>
        TypedResults.Ok(await service.ListAsync(installationId, ParseType(type), includeReplaced, ct));

    internal static async Task<Ok<DeviceResponse>> Get(Guid installationId, Guid deviceId, IDeviceService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(installationId, deviceId, ct));

    internal static async Task<Ok<IReadOnlyList<DeviceResponse>>> History(
        Guid installationId, Guid deviceId, IDeviceService service, CancellationToken ct) =>
        TypedResults.Ok(await service.HistoryAsync(installationId, deviceId, ct));

    internal static async Task<Created<DeviceResponse>> Add(
        Guid installationId, AddDeviceRequest request, IDeviceService service, CancellationToken ct)
    {
        var created = await service.AddAsync(installationId, request, ct);
        return TypedResults.Created($"/api/installations/{installationId}/devices/{created.Id}", created);
    }

    internal static async Task<Created<DeviceReplacementResponse>> Replace(
        Guid installationId, Guid deviceId, ReplaceDeviceRequest request, IDeviceService service, CancellationToken ct)
    {
        var result = await service.ReplaceAsync(installationId, deviceId, request, ct);
        return TypedResults.Created($"/api/installations/{installationId}/devices/{result.Replacement.Id}", result);
    }

    private static DeviceType? ParseType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        return Enum.TryParse<DeviceType>(type, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new RequestValidationException("type", $"Unknown device type '{type}'. Allowed: {string.Join(", ", Enum.GetNames<DeviceType>())}.");
    }
}
