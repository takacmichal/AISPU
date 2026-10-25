namespace SolarOps.Api.Features.Quotes;

/// <summary>Quote settings (<c>Quotes</c> section).</summary>
public sealed class QuoteOptions
{
    public const string SectionName = "Quotes";

    /// <summary>Validity of a sent quote when the request does not give one.</summary>
    public int DefaultValidityDays { get; set; } = 30;

    /// <summary>How often sent quotes past their validity are marked Expired.</summary>
    public int ExpirationCheckMinutes { get; set; } = 15;
}
