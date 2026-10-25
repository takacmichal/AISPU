namespace SolarOps.Api.Features.Auth;

/// <summary>Password reset settings (<c>PasswordReset</c> section).</summary>
public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>Lifetime of the reset token (acceptance criterion of #134).</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    /// <summary>Frontend page that receives <c>email</c> and <c>token</c> query parameters.</summary>
    public string ResetUrl { get; set; } = "http://localhost:5173/reset-password";
}

/// <summary>Delivers the password reset link to the user.</summary>
public interface IPasswordResetSender
{
    Task SendAsync(string email, string resetLink, CancellationToken cancellationToken);
}

/// <summary>Placeholder until a real e-mail provider exists: writes the link to the log (development only).</summary>
internal sealed class LoggingPasswordResetSender(ILogger<LoggingPasswordResetSender> logger) : IPasswordResetSender
{
    public Task SendAsync(string email, string resetLink, CancellationToken cancellationToken)
    {
        logger.LogWarning("Password reset link for {Email} (no e-mail provider configured): {Link}", email, resetLink);
        return Task.CompletedTask;
    }
}
