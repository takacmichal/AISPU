using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Installations;

/// <summary>Strings, MPPT inputs and panel layout of an installation (#84).</summary>
public sealed class StringsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IStringService, StringService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/installations/{installationId:guid}/strings").WithTags("Strings");

        group.MapGet("/", Layout)
            .WithName("GetInstallationStringLayout")
            .WithSummary("Panel layout: strings with panels and their summed power")
            .WithDescription("Strings ordered by number, each with its installed panels and the sum of their rated power (computed on read), plus installed panels not yet assigned to any string.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(InstallationRoles.Read));

        group.MapGet("/{stringId:guid}", Get)
            .WithName("GetInstallationString")
            .WithSummary("Get a string with its panels")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(InstallationRoles.Read));

        group.MapPost("/", Create)
            .WithName("CreateInstallationString")
            .WithSummary("Define a string (number as in telemetry, optional MPPT input)")
            .WithDescription("409 when the number is already used in the installation or the installation has been handed over.")
            .WithValidation<CreateStringRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(InstallationRoles.Field));

        group.MapPut("/{stringId:guid}", Update)
            .WithName("UpdateInstallationString")
            .WithSummary("Change number, name or MPPT input of a string")
            .WithValidation<UpdateStringRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(InstallationRoles.Field));

        group.MapDelete("/{stringId:guid}", Delete)
            .WithName("DeleteInstallationString")
            .WithSummary("Delete an empty string")
            .WithDescription("409 when the string still has panels.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(InstallationRoles.Field));

        group.MapPut("/{stringId:guid}/panels/{deviceId:guid}", AssignPanel)
            .WithName("AssignPanelToString")
            .WithSummary("Place an installed panel into the string (moves it from another string)")
            .WithDescription("A panel belongs to exactly one string. 409 for a replaced device or a device that is not a panel.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(InstallationRoles.Field));

        group.MapDelete("/{stringId:guid}/panels/{deviceId:guid}", UnassignPanel)
            .WithName("RemovePanelFromString")
            .WithSummary("Take a panel out of the string")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(InstallationRoles.Field));
    }

    internal static async Task<Ok<StringLayoutResponse>> Layout(Guid installationId, IStringService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetLayoutAsync(installationId, ct));

    internal static async Task<Ok<PanelStringResponse>> Get(Guid installationId, Guid stringId, IStringService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(installationId, stringId, ct));

    internal static async Task<Created<PanelStringResponse>> Create(
        Guid installationId, CreateStringRequest request, IStringService service, CancellationToken ct)
    {
        var created = await service.CreateAsync(installationId, request, ct);
        return TypedResults.Created($"/api/installations/{installationId}/strings/{created.Id}", created);
    }

    internal static async Task<Ok<PanelStringResponse>> Update(
        Guid installationId, Guid stringId, UpdateStringRequest request, IStringService service, CancellationToken ct) =>
        TypedResults.Ok(await service.UpdateAsync(installationId, stringId, request, ct));

    internal static async Task<NoContent> Delete(Guid installationId, Guid stringId, IStringService service, CancellationToken ct)
    {
        await service.DeleteAsync(installationId, stringId, ct);
        return TypedResults.NoContent();
    }

    internal static async Task<Ok<PanelStringResponse>> AssignPanel(
        Guid installationId, Guid stringId, Guid deviceId, IStringService service, CancellationToken ct) =>
        TypedResults.Ok(await service.AssignPanelAsync(installationId, stringId, deviceId, ct));

    internal static async Task<NoContent> UnassignPanel(
        Guid installationId, Guid stringId, Guid deviceId, IStringService service, CancellationToken ct)
    {
        await service.UnassignPanelAsync(installationId, stringId, deviceId, ct);
        return TypedResults.NoContent();
    }
}
