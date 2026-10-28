using System.Text.RegularExpressions;
using SolarOps.Api.Features.Customers;

namespace SolarOps.Api.Features.SupplyPoints;

/// <summary>
/// Supply point (odberné miesto) of a customer: where the grid connection is. Other modules reference it by
/// <see cref="Id"/> only. The EIC code (unique) is stored normalized: no whitespace, upper case.
/// </summary>
public sealed partial class SupplyPoint
{
    public const int NameMaxLength = 100;
    public const int EicLength = 16;
    public const int MinBreakerAmps = 1;
    public const int MaxBreakerAmps = 1000;

    // EF Core materialization.
    private SupplyPoint()
    {
        EicCode = string.Empty;
        Address = null!;
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>Optional label, e.g. "Rodinný dom".</summary>
    public string? Name { get; private set; }

    /// <summary>EIC code of the metering point, 16 characters.</summary>
    public string EicCode { get; private set; }

    public PostalAddress Address { get; private set; }

    /// <summary>Rated current of the main circuit breaker (hlavný istič) in amperes.</summary>
    public int MainBreakerAmps { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Removes whitespace and upper-cases an EIC code; null for blank input.</summary>
    public static string? NormalizeEic(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : WhitespaceRegex().Replace(value, string.Empty).ToUpperInvariant();

    /// <summary>
    /// Format check of a normalized EIC: 2 digits (issuing office), 1 letter (object type), 12 characters
    /// (letters, digits, '-') and a check character.
    /// </summary>
    public static bool IsValidEic(string eic) => EicRegex().IsMatch(eic);

    public static SupplyPoint Create(Guid customerId, string? name, string eicCode, PostalAddress address, int mainBreakerAmps, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        CustomerId = customerId,
        Name = name,
        EicCode = eicCode,
        Address = address,
        MainBreakerAmps = mainBreakerAmps,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Update(string? name, string eicCode, PostalAddress address, int mainBreakerAmps, DateTimeOffset now)
    {
        Name = name;
        EicCode = eicCode;
        Address = address;
        MainBreakerAmps = mainBreakerAmps;
        UpdatedAt = now;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^\d{2}[A-Z][A-Z0-9-]{12}[A-Z0-9]$")]
    private static partial Regex EicRegex();
}
