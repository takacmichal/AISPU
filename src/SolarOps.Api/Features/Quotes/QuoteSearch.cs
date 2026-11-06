using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.Persistence;
using SolarOps.Api.Infrastructure.Querying;

namespace SolarOps.Api.Features.Quotes;

internal static class QuoteSearch
{
    /// <summary>Customer name or the quote note contains the pattern (<see cref="SqlLike.Contains"/>). Quotes have no number to search by.</summary>
    public static IQueryable<Quote> Matching(this IQueryable<Quote> query, AppDbContext db, string pattern)
    {
        var customers = db.Set<Customer>().Where(CustomerSearch.NameMatches(pattern)).Select(x => x.Id);
        return query.Where(q => customers.Contains(q.CustomerId)
            || (q.Note != null && EF.Functions.ILike(q.Note, pattern, SqlLike.EscapeCharacter)));
    }
}
