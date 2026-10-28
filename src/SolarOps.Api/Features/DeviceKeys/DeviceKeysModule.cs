using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;

namespace SolarOps.Api.Features.DeviceKeys;

/// <summary>
/// Device authentication (#96): per-installation API keys for telemetry senders. Admin issues, rotates and revokes
/// keys; the <c>DeviceApiKey</c> scheme authenticates the ingest. ADR #218 covers user authentication only, so the
/// device key scheme is an addition to it.
/// </summary>
public sealed class DeviceKeysModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDeviceApiKeyService, DeviceApiKeyService>();

        // Registered next to the default (JWT) scheme without changing the default.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, DeviceApiKeyAuthenticationHandler>(DeviceApiKeyDefaults.Scheme, null);

        // The only place that names a scheme: a device sends just an API key, so the policy must evaluate this scheme
        // explicitly (the default scheme is JWT, or the test scheme in tests).
        services.AddAuthorizationBuilder().AddPolicy(DeviceApiKeyDefaults.PolicyName, policy => policy
            .AddAuthenticationSchemes(DeviceApiKeyDefaults.Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(DeviceApiKeyDefaults.InstallationClaim));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/installations/{installationId:guid}/api-keys").WithTags("DeviceKeys");

        group.MapGet("/", List)
            .WithName("ListInstallationApiKeys")
            .WithSummary("List API keys of an installation (metadata only)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        group.MapPost("/", Issue)
            .WithName("IssueInstallationApiKey")
            .WithSummary("Issue the API key of an installation")
            .WithDescription("The plaintext key is in the response only once. An installation has one active key: 409 when it already has one (use rotate).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        group.MapPost("/rotate", Rotate)
            .WithName("RotateInstallationApiKey")
            .WithSummary("Revoke the active API key and issue a new one")
            .WithDescription("Also renews access after a revocation. The old key stops working immediately.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        group.MapDelete("/{keyId:guid}", Revoke)
            .WithName("RevokeInstallationApiKey")
            .WithSummary("Revoke an API key")
            .WithDescription("The key keeps its record (history) but stops working. 409 when it is already revoked.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
    }

    internal static async Task<Ok<IReadOnlyList<DeviceApiKeyResponse>>> List(Guid installationId, IDeviceApiKeyService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(installationId, ct));

    internal static async Task<Created<IssuedDeviceApiKeyResponse>> Issue(Guid installationId, IDeviceApiKeyService service, CancellationToken ct)
    {
        var issued = await service.IssueAsync(installationId, ct);
        return TypedResults.Created($"/api/installations/{installationId}/api-keys", issued);
    }

    internal static async Task<Created<IssuedDeviceApiKeyResponse>> Rotate(Guid installationId, IDeviceApiKeyService service, CancellationToken ct)
    {
        var issued = await service.RotateAsync(installationId, ct);
        return TypedResults.Created($"/api/installations/{installationId}/api-keys", issued);
    }

    internal static async Task<Ok<DeviceApiKeyResponse>> Revoke(Guid installationId, Guid keyId, IDeviceApiKeyService service, CancellationToken ct) =>
        TypedResults.Ok(await service.RevokeAsync(installationId, keyId, ct));
}
