using Npgsql;
using Testcontainers.PostgreSql;

namespace SolarOps.Api.Tests.Infrastructure;

/// <summary>
/// One PostgreSQL container shared by all integration tests (xUnit collection fixture).
/// Each <see cref="ApiFactory"/> gets its own database, so tests never see each other's data.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    /// <summary>Connection string to the default database of the container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Connection string pointing to <paramref name="databaseName"/> on the shared container (created by EnsureCreated).</summary>
    public string CreateConnectionString(string databaseName) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>Put <c>[Collection(PostgresCollection.Name)]</c> on every integration test class.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
