using Microsoft.AspNetCore.Identity;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Auth;

/// <summary>Registration and login.</summary>
public interface IAuthService
{
    /// <summary>Creates a user with the <see cref="Roles.Customer"/> role.</summary>
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    /// <summary>Returns tokens, or null for any failure (unknown e-mail, wrong password, locked out).</summary>
    Task<TokenResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}

internal sealed class AuthService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IAccessTokenService accessTokens,
    IRefreshTokenService refreshTokens,
    ILogger<AuthService> logger) : IAuthService
{
    // Hash verified for unknown e-mails so the response time does not reveal whether an account exists.
    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString()));

    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var user = new AppUser { UserName = email, Email = email };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            throw ToException(created.Errors);
        }

        var roleResult = await userManager.AddToRoleAsync(user, Roles.Customer);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not assign role {Roles.Customer}: {string.Join("; ", roleResult.Errors.Select(e => e.Code))}");
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Registered user {UserId}", user.Id);

        return new UserResponse(user.Id, email, [Roles.Customer]);
    }

    public async Task<TokenResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            userManager.PasswordHasher.VerifyHashedPassword(new AppUser(), DummyPasswordHash.Value, request.Password);
            return null;
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
            {
                logger.LogWarning("Login rejected for locked out user {UserId}", user.Id);
            }

            return null;
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    private async Task<TokenResponse> IssueTokensAsync(AppUser user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var access = accessTokens.Create(user, roles);
        var refresh = await refreshTokens.IssueAsync(user.Id, cancellationToken);
        return new TokenResponse(access.Token, access.ExpiresInSeconds, refresh.Token);
    }

    private static Exception ToException(IEnumerable<IdentityError> errors)
    {
        var list = errors.ToList();
        if (list.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return new DomainException("An account with this e-mail cannot be created.");
        }

        return new RequestValidationException(IdentityErrorMapping.ToValidationErrors(list, passwordField: nameof(RegisterRequest.Password), otherField: nameof(RegisterRequest.Email)));
    }
}

/// <summary>Maps Identity errors to validation problem fields.</summary>
internal static class IdentityErrorMapping
{
    public static Dictionary<string, string[]> ToValidationErrors(IEnumerable<IdentityError> errors, string passwordField, string otherField) =>
        errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? passwordField : otherField, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal);
}
