using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.UserRoles;

/// <summary>Administrator management of user roles (#135).</summary>
public interface IUserRoleService
{
    Task<UserRolesListResponse> ListUsersAsync(string? q, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="NotFoundException"/> for an unknown user.</summary>
    Task<UserRolesResponse> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the roles of the user. Throws <see cref="RequestValidationException"/> for unknown roles,
    /// <see cref="NotFoundException"/> for an unknown user and <see cref="DomainException"/> when the change
    /// would leave the system without an administrator.
    /// </summary>
    Task<UserRolesResponse> SetUserRolesAsync(Guid userId, SetUserRolesRequest request, CancellationToken cancellationToken);
}

internal sealed class UserRoleService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ILogger<UserRoleService> logger) : IUserRoleService
{
    public const int MaxPageSize = 100;

    public async Task<UserRolesListResponse> ListUsersAsync(string? q, int page, int pageSize, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
        {
            errors["page"] = ["Page must be at least 1."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"PageSize must be between 1 and {MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{EscapeLike(q.Trim())}%";
            query = query.Where(u => u.Email != null && EF.Functions.ILike(u.Email, pattern, "\\"));
        }

        var total = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(cancellationToken);

        var ids = users.ConvertAll(u => u.Id);
        var assigned = await (
                from ur in db.UserRoles.AsNoTracking()
                join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where ids.Contains(ur.UserId)
                select new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken);
        var byUser = assigned.ToLookup(x => x.UserId, x => x.Name!);

        var items = users.ConvertAll(u =>
            new UserRolesResponse(u.Id, u.Email ?? string.Empty, [.. byUser[u.Id].Order(StringComparer.Ordinal)]));
        return new UserRolesListResponse(items, page, pageSize, total);
    }

    public async Task<UserRolesResponse> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userId);
        var roles = await userManager.GetRolesAsync(user);
        return new UserRolesResponse(user.Id, user.Email ?? string.Empty, [.. roles.Order(StringComparer.Ordinal)]);
    }

    public async Task<UserRolesResponse> SetUserRolesAsync(Guid userId, SetUserRolesRequest request, CancellationToken cancellationToken)
    {
        var desired = Normalize(request.Roles);

        // Serializable so two concurrent requests cannot both remove the last administrator.
        await using var transaction = await db.Database.BeginTransactionAsync(global::System.Data.IsolationLevel.Serializable, cancellationToken);

        var user = await FindAsync(userId);
        var current = await userManager.GetRolesAsync(user);

        var toAdd = desired.Except(current, StringComparer.Ordinal).ToList();
        var toRemove = current.Except(desired, StringComparer.Ordinal).ToList();

        if (toRemove.Contains(Roles.Admin) && (await userManager.GetUsersInRoleAsync(Roles.Admin)).Count <= 1)
        {
            throw new DomainException("The last administrator cannot lose the Admin role.");
        }

        if (toRemove.Count > 0)
        {
            Ensure(await userManager.RemoveFromRolesAsync(user, toRemove), "remove", userId);
        }

        if (toAdd.Count > 0)
        {
            Ensure(await userManager.AddToRolesAsync(user, toAdd), "add", userId);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Roles of user {UserId} changed: added [{Added}], removed [{Removed}]",
            userId, string.Join(", ", toAdd), string.Join(", ", toRemove));

        return new UserRolesResponse(user.Id, user.Email ?? string.Empty, [.. desired.Order(StringComparer.Ordinal)]);
    }

    /// <summary>Maps names to canonical role names (case-insensitive), removes duplicates, rejects unknown roles.</summary>
    private static List<string> Normalize(IEnumerable<string> names)
    {
        var result = new List<string>();
        var unknown = new List<string>();
        foreach (var name in names)
        {
            var canonical = Roles.All.FirstOrDefault(r => string.Equals(r, name?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                unknown.Add(name ?? string.Empty);
            }
            else if (!result.Contains(canonical))
            {
                result.Add(canonical);
            }
        }

        if (unknown.Count > 0)
        {
            throw new RequestValidationException(nameof(SetUserRolesRequest.Roles),
                $"Unknown role(s): {string.Join(", ", unknown.Select(u => $"'{u}'"))}. Allowed: {string.Join(", ", Roles.All)}.");
        }

        return result;
    }

    private async Task<AppUser> FindAsync(Guid userId) =>
        await userManager.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User", userId);

    private static void Ensure(IdentityResult result, string action, Guid userId)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not {action} roles of user {userId}: {string.Join("; ", result.Errors.Select(e => e.Code))}");
        }
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
