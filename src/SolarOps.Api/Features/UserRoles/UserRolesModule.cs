using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.UserRoles;

/// <summary>
/// Role assignment by an administrator (#135): <c>/api/roles</c> and <c>/api/users</c>.
/// Roles are defined in <see cref="Roles"/> (spike #207) and seeded by the Auth module.
/// </summary>
public sealed class UserRolesModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<IUserRoleService, UserRoleService>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/roles", ListRoles)
            .WithTags("Roles")
            .WithName("ListRoles")
            .WithSummary("List application roles")
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        var users = endpoints.MapGroup("/api/users").WithTags("Roles");

        users.MapGet("/", ListUsersAsync)
            .WithName("ListUsers")
            .WithSummary("List users with their roles")
            .WithDescription("Filter q matches e-mail (case-insensitive substring). Ordered by e-mail. Defaults: page 1, pageSize 20 (max 100).")
            .ProducesValidationProblem()
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        users.MapGet("/{id:guid}/roles", GetRolesAsync)
            .WithName("GetUserRoles")
            .WithSummary("Get roles of a user")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        users.MapPut("/{id:guid}/roles", SetRolesAsync)
            .WithName("SetUserRoles")
            .WithSummary("Set the roles of a user")
            .WithDescription("Replaces the complete set of roles (a user may have several); roles missing from the request are removed. "
                + "Role names are case-insensitive. The last administrator cannot lose Admin (409). "
                + "The change appears in the JWT issued by the next login or refresh (the current access token lives up to 15 min).")
            .WithValidation<SetUserRolesRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
    }

    internal static Ok<IReadOnlyList<string>> ListRoles() => TypedResults.Ok(Roles.All);

    internal static async Task<Ok<UserRolesListResponse>> ListUsersAsync(
        IUserRoleService service, CancellationToken ct, string? q = null, int page = 1, int pageSize = 20) =>
        TypedResults.Ok(await service.ListUsersAsync(q, page, pageSize, ct));

    internal static async Task<Ok<UserRolesResponse>> GetRolesAsync(Guid id, IUserRoleService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetUserRolesAsync(id, ct));

    internal static async Task<Ok<UserRolesResponse>> SetRolesAsync(
        Guid id, SetUserRolesRequest request, IUserRoleService service, CancellationToken ct) =>
        TypedResults.Ok(await service.SetUserRolesAsync(id, request, ct));
}
