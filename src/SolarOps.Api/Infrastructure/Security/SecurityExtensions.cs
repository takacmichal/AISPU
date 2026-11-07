using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using IPNetwork = System.Net.IPNetwork;

namespace SolarOps.Api.Infrastructure.Security;

/// <summary>Rate limiting for credential endpoints, the client IP behind a proxy and security response headers (#139, #379).</summary>
public static class SecurityExtensions
{
    /// <summary>Name of the rate limiting policy for credential endpoints (login, register, password reset).</summary>
    public const string AuthPolicy = "auth";

    /// <summary>
    /// Name of the rate limiting policy for token refresh. It is separate and higher, because refresh is regular
    /// traffic of signed-in users, and it must not use up the login attempts of an office behind one public IP.
    /// </summary>
    public const string RefreshPolicy = "auth-refresh";

    private const int DefaultAuthPermitLimit = 10;
    private const int DefaultRefreshPermitLimit = 60;
    private const int DefaultWindowSeconds = 60;

    public static IServiceCollection AddSecurityHardening(this IServiceCollection services, IConfiguration configuration)
    {
        var trustedProxies = TrustedProxies.Parse(configuration);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            // One proxy in front of the API: only the entry it added counts, anything further left is client input.
            options.ForwardLimit = 1;
            // Added to the loopback defaults. The list is never empty, an empty one would trust every sender.
            foreach (var proxy in trustedProxies.Addresses)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (var network in trustedProxies.Networks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AddIpPolicy(options, AuthPolicy, configuration, "RateLimiting:Auth", DefaultAuthPermitLimit);
            AddIpPolicy(options, RefreshPolicy, configuration, "RateLimiting:Refresh", DefaultRefreshPermitLimit);
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Rate limit exceeded. Try again later.",
                    },
                });
            };
        });

        return services;
    }

    private static void AddIpPolicy(
        RateLimiterOptions options, string name, IConfiguration configuration, string section, int defaultPermitLimit)
    {
        var permitLimit = configuration.GetValue($"{section}:PermitLimit", defaultPermitLimit);
        var window = TimeSpan.FromSeconds(configuration.GetValue($"{section}:WindowSeconds", DefaultWindowSeconds));

        options.AddPolicy(name, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
            }));
    }

    /// <summary>Adds security headers to every response, including errors, and HSTS outside Development.</summary>
    public static WebApplication UseSecurityHardening(this WebApplication app)
    {
        // First, so everything after it (the rate limiter above all) sees the real client IP.
        app.UseForwardedHeaders();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                return Task.CompletedTask;
            });
            await next();
        });

        app.UseRateLimiter();
        return app;
    }
}

/// <summary>Proxies allowed to set X-Forwarded-For, from ForwardedHeaders:KnownProxies and KnownNetworks.</summary>
internal sealed record TrustedProxies(IReadOnlyList<IPAddress> Addresses, IReadOnlyList<IPNetwork> Networks)
{
    /// <summary>A mistyped value must stop the startup; ignoring it would silently break the rate limit.</summary>
    public static TrustedProxies Parse(IConfiguration configuration)
    {
        var section = configuration.GetSection("ForwardedHeaders");
        var addresses = (section.GetSection("KnownProxies").Get<string[]>() ?? [])
            .Select(value => IPAddress.TryParse(value, out var address)
                ? address
                : throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains an invalid IP address '{value}'."));
        var networks = (section.GetSection("KnownNetworks").Get<string[]>() ?? [])
            .Select(value => IPNetwork.TryParse(value, out var network)
                ? network
                : throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks contains an invalid CIDR range '{value}'."));

        return new TrustedProxies([.. addresses], [.. networks]);
    }
}
