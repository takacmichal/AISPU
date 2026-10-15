namespace SolarOps.Api.Infrastructure.CurrentUser;

/// <summary>
/// The authenticated user of the current request. Every query on user-owned data must filter by <see cref="UserId"/>.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>User id from the <c>sub</c> / NameIdentifier claim; null when anonymous.</summary>
    Guid? UserId { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);

    /// <summary>Returns <see cref="UserId"/> or throws <see cref="UnauthorizedAccessException"/> when anonymous.</summary>
    Guid RequireUserId();
}
