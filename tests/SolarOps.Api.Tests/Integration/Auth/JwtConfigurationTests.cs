using Microsoft.AspNetCore.Hosting;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Auth;

[Collection(PostgresCollection.Name)]
public sealed class JwtConfigurationTests(PostgresFixture postgres)
{
    private sealed class MissingKeyFactory(PostgresFixture postgres) : ApiFactory(postgres)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Jwt:SigningKey", string.Empty);
        }
    }

    [Fact]
    public void Startup_FailsWithoutSigningKey()
    {
        using var factory = new MissingKeyFactory(postgres);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());

        Assert.Contains("SigningKey", exception.ToString());
    }
}
