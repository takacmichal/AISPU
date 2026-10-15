using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the API in-memory against a fresh database on the shared <see cref="PostgresFixture"/> container.
/// The schema is created with EnsureCreated when the host starts.
/// </summary>
/// <example>
/// <code>
/// [Collection(PostgresCollection.Name)]
/// public sealed class FooEndpointsTests(PostgresFixture postgres) : IDisposable
/// {
///     private readonly ApiFactory _factory = new(postgres);
///     public void Dispose() => _factory.Dispose();
///
///     [Fact]
///     public async Task Works()
///     {
///         using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
///         var response = await client.GetAsync("/api/foo");
///     }
/// }
/// </code>
/// </example>
public class ApiFactory(PostgresFixture postgres, string? databaseName = null) : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Testing";

    /// <summary>Unique database name of this factory.</summary>
    public string DatabaseName { get; } = databaseName ?? $"test_{Guid.NewGuid():N}";

    public string ConnectionString => postgres.CreateConnectionString(DatabaseName);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        // UseSetting is applied before Program.cs reads configuration.
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
            // PostConfigure wins over defaults set by the auth module (JWT), so tests authenticate via headers.
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        host.Services.EnsureDatabaseCreatedAsync().GetAwaiter().GetResult();
        return host;
    }

    /// <summary>Creates an HttpClient for the in-memory API (base address http://localhost).</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    /// <summary>
    /// Creates an HttpClient authenticated as <paramref name="userId"/> with the given roles
    /// (see <see cref="TestAuthHandler"/>), e.g. <c>CreateApiClient(userId, Roles.Sales)</c>.
    /// </summary>
    public HttpClient CreateApiClient(Guid userId, params string[] roles)
    {
        var client = CreateApiClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(",", roles));
        }

        return client;
    }

    /// <summary>Runs <paramref name="action"/> with a scoped <see cref="AppDbContext"/> (seeding, assertions).</summary>
    public async Task ExecuteDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Runs <paramref name="query"/> with a scoped <see cref="AppDbContext"/> and returns its result.</summary>
    public async Task<T> ExecuteDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
