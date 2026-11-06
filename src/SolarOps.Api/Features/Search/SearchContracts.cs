using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace SolarOps.Api.Features.Search;

/// <summary>Query string of <c>GET /api/search</c>: search text (at least 2 characters) and hits per group.</summary>
/// <param name="Q">Case-insensitive substring to look for, 2 to 200 characters after trimming.</param>
/// <param name="Limit">Maximum hits per group, 1 to 10, default 5.</param>
public sealed record SearchQuery(
    [FromQuery(Name = "q")] string? Q = null,
    [FromQuery(Name = "limit")] int Limit = SearchQuery.DefaultLimit) : IValidatableObject
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 10;
    public const int MinTermLength = 2;
    public const int MaxTermLength = 200;

    public string Term => Q?.Trim() ?? string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Term.Length is < MinTermLength or > MaxTermLength)
        {
            yield return new ValidationResult($"Q must be {MinTermLength} to {MaxTermLength} characters.", ["q"]);
        }

        if (Limit is < 1 or > MaxLimit)
        {
            yield return new ValidationResult($"Limit must be between 1 and {MaxLimit}.", ["limit"]);
        }
    }
}

/// <summary>One search hit. The frontend builds the link from the group and <c>Id</c>.</summary>
/// <param name="Id">Id of the customer, quote, installation or ticket.</param>
/// <param name="Title">Main line, e.g. the customer name, installation name or ticket title.</param>
/// <param name="Subtitle">Secondary line (e-mail, status and customer); may be null.</param>
public sealed record SearchHit(Guid Id, string Title, string? Subtitle);

/// <summary>
/// Hits grouped by type. A group is null when the caller's role may not read that module (the customers group is
/// never returned to a customer account); otherwise it is a list, possibly empty, with at most <c>limit</c> hits.
/// </summary>
public sealed record SearchResponse(
    IReadOnlyList<SearchHit>? Customers,
    IReadOnlyList<SearchHit>? Quotes,
    IReadOnlyList<SearchHit>? Installations,
    IReadOnlyList<SearchHit>? Tickets);
