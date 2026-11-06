using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.SupplyPoints;
using SolarOps.Api.Infrastructure.Persistence;
using SolarOps.Api.Infrastructure.Querying;

namespace SolarOps.Api.Features.Installations;

internal static class InstallationSearch
{
    /// <summary>Installation name, supply point name or address, or customer name contains the pattern (<see cref="SqlLike.Contains"/>).</summary>
    public static IQueryable<Installation> Matching(this IQueryable<Installation> query, AppDbContext db, string pattern)
    {
        var customers = db.Set<Customer>().Where(CustomerSearch.NameMatches(pattern)).Select(x => x.Id);
        var supplyPoints = db.Set<SupplyPoint>().Where(sp =>
                (sp.Name != null && EF.Functions.ILike(sp.Name, pattern, SqlLike.EscapeCharacter))
                || EF.Functions.ILike(sp.Address.Street, pattern, SqlLike.EscapeCharacter)
                || EF.Functions.ILike(sp.Address.City, pattern, SqlLike.EscapeCharacter))
            .Select(sp => sp.Id);
        return query.Where(x => EF.Functions.ILike(x.Name, pattern, SqlLike.EscapeCharacter)
            || customers.Contains(x.CustomerId)
            || supplyPoints.Contains(x.SupplyPointId));
    }
}
