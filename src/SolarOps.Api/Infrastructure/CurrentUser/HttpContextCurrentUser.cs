using System.Security.Claims;

namespace SolarOps.Api.Infrastructure.CurrentUser;

/// <summary><see cref="ICurrentUser"/> backed by <see cref="HttpContext.User"/>.</summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray() ?? [];

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public Guid RequireUserId() => UserId ?? throw new UnauthorizedAccessException("User is not authenticated.");
}
