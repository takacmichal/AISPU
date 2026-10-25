using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Customers;

/// <summary>Which customer data the current user may see. Apply it in the query, never after loading.</summary>
/// <param name="IsRestricted">True for a customer account: it sees only the data of its own customer.</param>
/// <param name="CustomerId">Id of the customer linked to the account; null when restricted and not linked.</param>
public sealed record DataScope(bool IsRestricted, Guid? CustomerId)
{
    /// <summary>
    /// Customer id to filter by when <see cref="IsRestricted"/>. An account without a linked customer gets
    /// <see cref="Guid.Empty"/>, which matches no customer, so it sees nothing.
    /// </summary>
    public Guid OwnCustomerId => CustomerId ?? Guid.Empty;
}

/// <summary>Resolves the <see cref="DataScope"/> of the current user (#137).</summary>
public interface ICustomerScope
{
    Task<DataScope> GetAsync(CancellationToken ct);
}

internal sealed class CustomerScope(AppDbContext db, ICurrentUser currentUser) : ICustomerScope
{
    private DataScope? _scope;

    public async Task<DataScope> GetAsync(CancellationToken ct)
    {
        if (_scope is not null)
        {
            return _scope;
        }

        // A user who also has a back-office role is not limited to one customer.
        var isCustomerOnly = currentUser.IsInRole(Roles.Customer)
            && !currentUser.Roles.Any(r => r != Roles.Customer);
        if (!isCustomerOnly)
        {
            return _scope = new DataScope(false, null);
        }

        var userId = currentUser.RequireUserId();
        var customerId = await db.Set<Customer>().AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        return _scope = new DataScope(true, customerId);
    }
}
