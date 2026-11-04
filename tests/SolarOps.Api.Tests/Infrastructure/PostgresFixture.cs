using Npgsql;
using Testcontainers.PostgreSql;

namespace SolarOps.Api.Tests.Infrastructure;

/// <summary>
/// Gives integration tests a PostgreSQL server. All test classes share ONE container for the whole test run
/// (started lazily, removed at process exit), so classes can run in parallel. The schema is migrated once into a
/// template database; every <see cref="ApiFactory"/> then gets its own database as a cheap copy of that template,
/// so tests never see each other's data and never replay the migrations.
/// </summary>
/// <remarks>
/// Put <c>IClassFixture&lt;PostgresFixture&gt;</c> on every integration test class. The old
/// <c>[Collection(PostgresCollection.Name)]</c> still works but runs all its classes one after another.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Migrated, empty database that new test databases are copied from.</summary>
    internal const string TemplateName = "solarops_template";

    private static readonly Lazy<Task<SharedServer>> Shared = new(SharedServer.StartAsync);

    private SharedServer _server = null!;

    /// <summary>Connection string to the default database of the container.</summary>
    public string ConnectionString => _server.ConnectionString;

    /// <summary>Connection string pointing to <paramref name="databaseName"/> on the shared container.</summary>
    public string CreateConnectionString(string databaseName) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;

    public async Task InitializeAsync() => _server = await Shared.Value;

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Creates <paramref name="databaseName"/> as a copy of the migrated template.</summary>
    internal void CreateDatabaseFromTemplate(string databaseName) => _server.CreateDatabaseFromTemplate(databaseName);

    private sealed class SharedServer(PostgreSqlContainer container)
    {
        // CREATE DATABASE ... TEMPLATE needs exclusive access to the template, so copies are made one at a time.
        private readonly object _copyLock = new();

        public string ConnectionString => container.GetConnectionString();

        public static async Task<SharedServer> StartAsync()
        {
            var container = new PostgreSqlBuilder("postgres:16-alpine")
                // Throw-away test data: skip durability for speed, and allow many parallel test databases.
                .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off", "-c", "max_connections=1000")
                .Build();
            await container.StartAsync();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => container.DisposeAsync().AsTask().GetAwaiter().GetResult();

            var server = new SharedServer(container);
            await server.CreateTemplateAsync();
            return server;
        }

        private async Task CreateTemplateAsync()
        {
            // Building the real host applies the committed migrations (and the startup seeding) to the template.
            var bootstrap = new PostgresFixture { _server = this };
            using (var factory = new ApiFactory(bootstrap, TemplateName))
            {
                _ = factory.Services;
            }

            // The template must have no open connections when it is copied.
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE \"{TemplateName}\" IS_TEMPLATE true";
            await command.ExecuteNonQueryAsync();
        }

        public void CreateDatabaseFromTemplate(string databaseName)
        {
            lock (_copyLock)
            {
                using var connection = new NpgsqlConnection(ConnectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{TemplateName}\"";
                command.ExecuteNonQuery();
            }
        }
    }
}

/// <summary>Legacy: classes in this collection run one after another. Prefer <c>IClassFixture&lt;PostgresFixture&gt;</c>.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
