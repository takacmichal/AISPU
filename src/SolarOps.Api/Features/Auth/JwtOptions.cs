using System.ComponentModel.DataAnnotations;
using System.Text;

namespace SolarOps.Api.Features.Auth;

/// <summary>
/// JWT settings (section <c>Jwt</c>). <see cref="SigningKey"/> is a secret: set it via
/// <c>Jwt__SigningKey</c> env variable or user-secrets; only appsettings.Development.json has a local dev key.
/// </summary>
public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 needs at least 256 bits of key material.</summary>
    public const int MinSigningKeyBytes = 32;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Required]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access token lifetime; spike #218 caps it at 15 minutes.</summary>
    [Range(1, 15)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 30)]
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Tolerated clock difference when validating token lifetime.</summary>
    [Range(0, 120)]
    public int ClockSkewSeconds { get; set; } = 30;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenDays);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < MinSigningKeyBytes)
        {
            yield return new ValidationResult(
                $"Jwt:SigningKey must have at least {MinSigningKeyBytes} bytes (set it via the Jwt__SigningKey environment variable or user-secrets).",
                [nameof(SigningKey)]);
        }
    }
}
