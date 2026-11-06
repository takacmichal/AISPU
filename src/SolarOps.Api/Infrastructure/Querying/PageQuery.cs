using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace SolarOps.Api.Infrastructure.Querying;

/// <summary>
/// Common query string of list endpoints: paging, free-text search and sorting. Bind it with <c>[AsParameters]</c> and add
/// <c>.WithValidation&lt;PageQuery&gt;()</c>. The sort key is checked against the endpoint's whitelist in
/// <see cref="QueryableExtensions.ApplySort{T}"/>. Error keys are the query parameter names.
/// </summary>
/// <param name="Page">1-based page number, default 1.</param>
/// <param name="PageSize">Items per page, 1 to 100, default 20.</param>
/// <param name="Q">Case-insensitive substring search, up to 200 characters.</param>
/// <param name="SortBy">Sort key; the allowed keys depend on the endpoint.</param>
/// <param name="SortDir"><c>asc</c> or <c>desc</c>; the default depends on the endpoint.</param>
public sealed record PageQuery(
    [FromQuery(Name = "page")] int Page = PageQuery.DefaultPage,
    [FromQuery(Name = "pageSize")] int PageSize = PageQuery.DefaultPageSize,
    [FromQuery(Name = "q")] string? Q = null,
    [FromQuery(Name = "sortBy")] string? SortBy = null,
    [FromQuery(Name = "sortDir")] string? SortDir = null) : IValidatableObject
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 200;

    /// <summary>Trimmed search text, or null when empty.</summary>
    public string? SearchTerm => string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();

    /// <summary>Rows to skip; saturates instead of overflowing for absurd page numbers.</summary>
    public int Skip => (int)Math.Min((long)(Math.Max(Page, 1) - 1) * PageSize, int.MaxValue);

    /// <summary>True/false when the caller chose a direction, null when it left the endpoint default.</summary>
    public bool? Descending => string.IsNullOrWhiteSpace(SortDir) ? null : SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Page < 1)
        {
            yield return new ValidationResult("Page must be at least 1.", ["page"]);
        }

        if (PageSize is < 1 or > MaxPageSize)
        {
            yield return new ValidationResult($"PageSize must be between 1 and {MaxPageSize}.", ["pageSize"]);
        }

        if (Q is { Length: > MaxSearchLength })
        {
            yield return new ValidationResult($"Q must be at most {MaxSearchLength} characters.", ["q"]);
        }

        if (!string.IsNullOrWhiteSpace(SortDir)
            && !SortDir.Equals("asc", StringComparison.OrdinalIgnoreCase)
            && !SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("SortDir must be asc or desc.", ["sortDir"]);
        }
    }
}
