using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Infrastructure.Querying;

namespace SolarOps.Api.Features.Customers;

/// <summary>Reusable SQL predicates for finding customers by text; other modules use them to search by customer name.</summary>
internal static class CustomerSearch
{
    /// <summary>Name (surname first, or company name) or "first last" contains the pattern (<see cref="SqlLike.Contains"/>).</summary>
    public static Expression<Func<Customer, bool>> NameMatches(string pattern) => c =>
        EF.Functions.ILike(c.SortName, pattern, SqlLike.EscapeCharacter)
        || EF.Functions.ILike(c.FirstName + " " + c.LastName, pattern, SqlLike.EscapeCharacter);

    /// <summary>Name, e-mail, phone or IČO contains the pattern.</summary>
    public static Expression<Func<Customer, bool>> Matches(string pattern) => c =>
        EF.Functions.ILike(c.SortName, pattern, SqlLike.EscapeCharacter)
        || EF.Functions.ILike(c.FirstName + " " + c.LastName, pattern, SqlLike.EscapeCharacter)
        || EF.Functions.ILike(c.Email, pattern, SqlLike.EscapeCharacter)
        || (c.Phone != null && EF.Functions.ILike(c.Phone, pattern, SqlLike.EscapeCharacter))
        || (c.Ico != null && EF.Functions.ILike(c.Ico, pattern, SqlLike.EscapeCharacter));
}
