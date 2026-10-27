using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.UserRoles;

/// <summary>Payload for <c>PUT /api/users/{id}/roles</c>: the complete set of roles the user should have.</summary>
public sealed record SetUserRolesRequest
{
    /// <summary>Role names (case-insensitive), at least one. Roles missing from the list are removed.</summary>
    // MaxLength must be a constant: it equals Roles.All.Count (the Normalize step dedupes, so more entries are never useful).
    [Required, MinLength(1), MaxLength(6)]
    public required IReadOnlyList<string> Roles { get; init; }
}

/// <summary>User with the roles currently assigned.</summary>
public sealed record UserRolesResponse(Guid Id, string Email, IReadOnlyList<string> Roles);

/// <summary>Page of users returned by <c>GET /api/users</c>.</summary>
public sealed record UserRolesListResponse(
    IReadOnlyList<UserRolesResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
