using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;

namespace SolarOps.Api.Tests.Integration.Infrastructure;

/// <summary>
/// Verifies the contract of <see cref="ApiFactory"/> that the whole integration suite relies on (#171):
/// every factory owns a separate database created by EF migrations, so tests cannot see each other's data.
/// </summary>
public sealed class DatabaseIsolationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly ApiFactory _first = new(postgres);
    private readonly ApiFactory _second = new(postgres);

    public void Dispose()
    {
        _first.Dispose();
        _second.Dispose();
    }

    [Fact]
    public void Factories_UseDifferentDatabases()
    {
        Assert.StartsWith("test_", _first.DatabaseName, StringComparison.Ordinal);
        Assert.StartsWith("test_", _second.DatabaseName, StringComparison.Ordinal);
        Assert.NotEqual(_first.DatabaseName, _second.DatabaseName);
        Assert.NotEqual(_first.ConnectionString, _second.ConnectionString);
    }

    [Fact]
    public async Task NewFactory_StartsWithEmptyDatabase()
    {
        using var client = _first.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

        var customers = await client.GetJsonAsync<CustomerListResponse>("/api/customers");

        Assert.Empty(customers.Items);
        Assert.Equal(0, customers.TotalCount);
    }

    [Fact]
    public async Task DataWrittenThroughOneFactory_IsNotVisibleThroughAnother()
    {
        using var first = _first.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        using var second = _second.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

        var customer = await CustomerTestData.CreateAsync(first, CustomerTestData.Person());
        var firstCustomers = await first.GetJsonAsync<CustomerListResponse>("/api/customers");
        var secondCustomers = await second.GetJsonAsync<CustomerListResponse>("/api/customers");

        Assert.Contains(firstCustomers.Items, item => item.Id == customer.Id);
        Assert.DoesNotContain(secondCustomers.Items, item => item.Id == customer.Id);
        Assert.Empty(secondCustomers.Items);
    }

    [Fact]
    public async Task EachFactory_HasAllMigrationsApplied()
    {
        var firstApplied = await _first.ExecuteDbAsync(db => db.Database.GetAppliedMigrationsAsync());
        var secondApplied = await _second.ExecuteDbAsync(db => db.Database.GetAppliedMigrationsAsync());
        var allMigrations = await _first.ExecuteDbAsync(db => Task.FromResult(db.Database.GetMigrations()));

        Assert.NotEmpty(firstApplied);
        Assert.Equal(allMigrations, firstApplied);
        Assert.Equal(firstApplied, secondApplied);
    }

    [Fact]
    public void EveryIntegrationTestClass_SharesThePostgresContainer()
    {
        var testClasses = typeof(DatabaseIsolationTests).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("SolarOps.Api.Tests.Integration", StringComparison.Ordinal) == true)
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetConstructors().Any(ctor =>
                ctor.GetParameters().Any(parameter => parameter.ParameterType == typeof(PostgresFixture))))
            .Where(type => type.GetMethods().Any(method =>
                method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0
                || method.GetCustomAttributes(typeof(TheoryAttribute), inherit: true).Length > 0))
            .ToList();

        Assert.NotEmpty(testClasses);
        foreach (var type in testClasses)
        {
            var usesClassFixture = type.GetInterfaces().Contains(typeof(IClassFixture<PostgresFixture>));
            var usesLegacyCollection = type.GetCustomAttributesData()
                .Where(data => data.AttributeType == typeof(CollectionAttribute))
                .Any(data => data.ConstructorArguments.Count > 0 && data.ConstructorArguments[0].Value as string == PostgresCollection.Name);
            Assert.True(
                usesClassFixture || usesLegacyCollection,
                $"{type.FullName} must implement IClassFixture<PostgresFixture> (parallel) or use [Collection({PostgresCollection.Name})] (serial).");
        }
    }
}
