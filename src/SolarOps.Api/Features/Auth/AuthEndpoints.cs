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

    /// <summary>Same response for every credential failure, so the reason is never revealed.</summary>
    internal static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Invalid e-mail or password.");
}
