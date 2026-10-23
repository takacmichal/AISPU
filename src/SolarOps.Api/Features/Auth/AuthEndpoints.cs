using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Auth;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .WithSummary("Register a customer account")
            .WithDescription("Creates a user with the Customer role. Internal roles are assigned by an administrator.")
            .AllowAnonymous()
            .WithValidation<RegisterRequest>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Log in with e-mail and password")
            .WithDescription("Returns a JWT access token (15 min) and a single-use refresh token (7 days). "
                + "Any failure (unknown e-mail, wrong password, locked out account) returns the same 401.")
            .AllowAnonymous()
            .WithValidation<LoginRequest>();

        group.MapPost("/refresh", RefreshAsync)
            .WithName("Refresh")
            .WithSummary("Exchange a refresh token for a new token pair")
            .WithDescription("The refresh token is single-use: it is replaced by the returned one. Reusing an already "
                + "exchanged token revokes the whole session (401), except for a repeated request within a few seconds "
                + "(concurrent tabs), which gets another valid token. Unknown, expired and revoked tokens return 401.")
            .AllowAnonymous()
            .WithValidation<RefreshTokenRequest>();

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Log out (revoke the refresh token and its session)")
            .WithDescription("Needs no access token, so it works after the access token expired. Returns 204 even "
                + "for unknown or already revoked tokens. Already issued access tokens stay valid until they expire (max. 15 min).")
            .AllowAnonymous()
            .WithValidation<RefreshTokenRequest>();

        return endpoints;
    }

    internal static async Task<Created<UserResponse>> RegisterAsync(
        RegisterRequest request, IAuthService auth, CancellationToken cancellationToken)
    {
        var user = await auth.RegisterAsync(request, cancellationToken);
        return TypedResults.Created("/api/auth/me", user);
    }

    internal static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, IAuthService auth, CancellationToken cancellationToken)
    {
        var tokens = await auth.LoginAsync(request, cancellationToken);
        return tokens is null ? InvalidCredentials() : TypedResults.Ok(tokens);
    }

    internal static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> RefreshAsync(
        RefreshTokenRequest request, IAuthService auth, CancellationToken cancellationToken)
    {
        var tokens = await auth.RefreshAsync(request, cancellationToken);
        return tokens is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Invalid refresh token.")
            : TypedResults.Ok(tokens);
    }

    internal static async Task<NoContent> LogoutAsync(
        RefreshTokenRequest request, IAuthService auth, CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(request, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Same response for every credential failure, so the reason is never revealed.</summary>
    internal static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Invalid e-mail or password.");
}
