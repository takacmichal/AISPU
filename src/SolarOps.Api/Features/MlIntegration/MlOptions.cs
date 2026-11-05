using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>
/// ML service settings (<c>Ml</c> section, ADR #196). With an empty <see cref="BaseUrl"/> the service counts as
/// unavailable: the API still starts and an analysis answers 503.
/// </summary>
public sealed class MlOptions
{
    public const string SectionName = "Ml";

    /// <summary>Base address of the FastAPI service, e.g. <c>http://ml:8000</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>HTTP timeout of one attempt; covers the cold start of the scale-to-zero container (ADR #196: 15 s).</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Attempts per call when the service is unavailable (ADR #196: one retry).</summary>
    [Range(1, 5)]
    public int MaxAttempts { get; set; } = 2;

    /// <summary>Delay between failed attempts.</summary>
    [Range(0, 60)]
    public int RetryDelaySeconds { get; set; } = 1;

    /// <summary>Finished hours sent to the detection on every run. An ongoing alarm extends the window back to its start.</summary>
    [Range(2, 2208)]
    public int DetectionLookbackHours { get; set; } = 72;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>Empty (not configured) or an absolute http/https address.</summary>
    public bool HasValidBaseUrl() =>
        !IsConfigured
        || (Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}
