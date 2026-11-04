namespace SolarOps.Api.Infrastructure.Security;

/// <summary>
/// Browser access for the frontend (#138): only origins explicitly listed in <c>Cors:AllowedOrigins</c> are allowed.
/// The list is read from configuration, so Azure sets it with the standard environment variable
/// <c>Cors__AllowedOrigins__0</c> (and further indexes).
/// </summary>
public static class CorsExtensions
{
    /// <summary>Name of the policy applied by <c>UseCors</c> in Program.cs.</summary>
    public const string FrontendPolicy = "Frontend";

    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(FrontendPolicy, policy => policy
            .WithOrigins(origins.Select(NormalizeOrigin).ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            // The SPA authenticates with a bearer token, but credentials stay enabled for a future
            // HttpOnly refresh cookie (ADR #218); that requires explicit origins, never a wildcard.
            .AllowCredentials()));

        return services;
    }

    /// <summary>
    /// Normalizes a configured origin to <c>scheme://host[:port]</c>. A value with a path, query, fragment,
    /// user info or another scheme is rejected at startup instead of silently never matching in the browser.
    /// </summary>
    internal static string NormalizeOrigin(string origin)
    {
        var value = origin?.Trim() ?? string.Empty;

        if (value == "*")
        {
            throw new InvalidOperationException(
                "Cors:AllowedOrigins cannot contain '*': credentials are allowed, so list explicit origins.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                $"Cors:AllowedOrigins contains an invalid origin '{origin}'. " +
                "Use scheme://host[:port] without a path, e.g. https://app.example.com.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}
