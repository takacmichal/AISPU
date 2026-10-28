using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.DeviceKeys;

/// <summary>Names used by the device key authentication (#96).</summary>
public static class DeviceApiKeyDefaults
{
    public const string Scheme = "DeviceApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string PolicyName = "DeviceIngest";

    /// <summary>Claim with the installation the presented key belongs to.</summary>
    public const string InstallationClaim = "installation_id";
}

/// <summary>
/// Authenticates a device by the <c>X-Api-Key</c> header. No header: no result (anonymous, 401 at the challenge);
/// unknown, malformed or revoked key: failure (401). The principal carries only the <c>installation_id</c> claim.
/// </summary>
internal sealed class DeviceApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(DeviceApiKeyDefaults.HeaderName, out var header))
        {
            return AuthenticateResult.NoResult();
        }

        if (!DeviceApiKey.TryParse(header.ToString(), out var id, out var secret))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var key = await db.Set<DeviceApiKey>().AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, Context.RequestAborted);
        if (key is null || !key.IsActive || !key.Matches(secret))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var identity = new ClaimsIdentity(
            [new Claim(DeviceApiKeyDefaults.InstallationClaim, key.InstallationId.ToString())], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
