using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.Auth;

/// <summary>Self-registration of a customer account.</summary>
public sealed record RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    /// <summary>Must satisfy the password policy (min. 10 characters, upper and lower case letter, digit).</summary>
    [Required, MaxLength(128)]
    public required string Password { get; init; }
}

/// <summary>E-mail and password login.</summary>
public sealed record LoginRequest
{
    [Required, MaxLength(256)]
    public required string Email { get; init; }

    [Required, MaxLength(128)]
    public required string Password { get; init; }
}

/// <summary>Request for a password reset link.</summary>
public sealed record ForgotPasswordRequest
{
    [Required, MaxLength(256)]
    public required string Email { get; init; }
}

/// <summary>Sets a new password with the token from the reset link.</summary>
public sealed record ResetPasswordRequest
{
    [Required, MaxLength(256)]
    public required string Email { get; init; }

    /// <summary>Token from the reset link.</summary>
    [Required, MaxLength(2048)]
    public required string Token { get; init; }

    /// <summary>Must satisfy the password policy (min. 10 characters, upper and lower case letter, digit).</summary>
    [Required, MaxLength(128)]
    public required string NewPassword { get; init; }
}

/// <summary>Refresh token presented to <c>/api/auth/refresh</c> or <c>/api/auth/logout</c>.</summary>
public sealed record RefreshTokenRequest
{
    [Required, MaxLength(256)]
    public required string RefreshToken { get; init; }
}

/// <summary>
/// Tokens returned after login (and refresh). The access token is a JWT for the <c>Authorization: Bearer</c> header;
/// the refresh token is opaque, single-use and valid for 7 days.
/// </summary>
/// <param name="AccessToken">Signed JWT.</param>
/// <param name="ExpiresIn">Access token lifetime in seconds.</param>
/// <param name="RefreshToken">Opaque refresh token; keep it out of persistent storage.</param>
public sealed record TokenResponse(string AccessToken, int ExpiresIn, string RefreshToken);

/// <summary>Account of a user.</summary>
public sealed record UserResponse(Guid Id, string Email, IReadOnlyList<string> Roles);
