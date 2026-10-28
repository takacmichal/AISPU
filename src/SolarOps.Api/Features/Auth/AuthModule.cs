using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Auth;

/// <summary>
/// Authentication (spike #218): ASP.NET Core Identity (EF stores) + JWT Bearer access tokens
/// + rotating refresh tokens. Endpoints under <c>/api/auth</c>.
/// </summary>
public sealed class AuthModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddIdentityCore<AppUser>(ConfigureIdentity)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) => ConfigureBearer(bearer, jwtOptions.Value));

        // Role policies and the authenticated-by-default fallback (#136); modules reference the policy names only.
        services.AddAuthorization(AuthorizationPolicies.Configure);

        services.AddScoped<IAccessTokenService, AccessTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddOptions<PasswordResetOptions>().Bind(configuration.GetSection(PasswordResetOptions.SectionName));
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = PasswordResetOptions.TokenLifetime);
        // No e-mail provider yet: the default only logs the link. Register another IPasswordResetSender to replace it.
        services.TryAddSingleton<IPasswordResetSender, LoggingPasswordResetSender>();
        services.AddScoped<IdentitySeeder>();
        services.AddHostedService<IdentitySeedHostedService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapAuthEndpoints();

    internal static void ConfigureIdentity(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 4;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        // No e-mail confirmation flow in Sprint 1 (no real e-mail provider yet).
        options.SignIn.RequireConfirmedEmail = false;
    }

    internal static void ConfigureBearer(JwtBearerOptions bearer, JwtOptions jwt)
    {
        // Default inbound claim mapping stays on: "sub" -> NameIdentifier and "role" -> ClaimTypes.Role,
        // which is what ICurrentUser and RequireRole read.
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = AccessTokenService.CreateSigningKey(jwt),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
        };
    }
}
