using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SolarOps.Api.Features.Auth;

namespace SolarOps.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="ApiFactory"/> that authenticates with the real JWT Bearer scheme instead of <see cref="TestAuthHandler"/>.
/// Use it for end-to-end auth flows (register, login, Authorization: Bearer).
/// </summary>
public class JwtApiFactory(PostgresFixture postgres) : ApiFactory(postgres)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            // Registered after the base PostConfigure, so JWT becomes the default again.
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
            }));
    }

    /// <summary>Creates the application roles (the startup seeding runs before the test schema exists).</summary>
    public async Task SeedIdentityAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
    }
}
