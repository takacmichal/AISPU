using System.Net;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Contracts;

namespace SolarOps.Api.Tests.Integration.Customers;

/// <summary>Audit trail, GDPR anonymization and protection of personal data (#140).</summary>
public sealed class CustomerPrivacyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose() => _factory.Dispose();

    private HttpClient Sales(Guid userId) => _factory.CreateApiClient(userId, Roles.Sales);

    private HttpClient Admin() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);

    [Fact]
    public async Task CreateAndUpdate_AreAudited_WithActorAndChangedFieldNamesOnly()
    {
        var salesId = Guid.CreateVersion7();
        using var sales = Sales(salesId);
        var customer = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person());

        var update = new UpdateCustomerRequest
        {
            Type = CustomerType.Person,
            FirstName = "Ján",
            LastName = "Tajná",
            Email = "jan.novak@example.sk",
            Phone = "+421 911 111 111",
            Address = CustomerTestData.Address(),
        };
        using var updated = await sales.PutJsonAsync($"/api/customers/{customer.Id}", update);
        updated.EnsureSuccessStatusCode();

        using var admin = Admin();
        var body = await admin.GetStringAsync($"/api/customers/{customer.Id}/audit");
        var audit = await admin.GetJsonAsync<List<CustomerAuditEntryResponse>>($"/api/customers/{customer.Id}/audit");

        Assert.Equal([CustomerAction.Created, CustomerAction.Updated], audit.Select(e => e.Action));
        Assert.All(audit, entry => Assert.Equal(salesId, entry.PerformedBy));
        Assert.Contains(nameof(Customer.LastName), audit[1].Details);
        Assert.Contains(nameof(Customer.Phone), audit[1].Details);
        Assert.DoesNotContain("Tajná", body, StringComparison.Ordinal);
        Assert.DoesNotContain("jan.novak@example.sk", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("911 111 111", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymize_ErasesPersonalData_UnlinksTheAccount_AndKeepsTheAuditTrail()
    {
        var user = new AppUser { UserName = "zakaznik@example.sk", Email = "zakaznik@example.sk" };
        await _factory.ExecuteDbAsync(async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });

        using var sales = Sales(Guid.CreateVersion7());
        var customer = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person() with { UserId = user.Id });

        using var admin = Admin();
        using var response = await admin.PostAsync($"/api/customers/{customer.Id}/anonymize", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var anonymized = await response.ReadJsonAsync<CustomerResponse>();
        Assert.Equal(CustomerStatus.Archived, anonymized.Status);
        Assert.NotNull(anonymized.AnonymizedAt);
        Assert.Equal(Customer.AnonymizedName, anonymized.DisplayName);
        Assert.Null(anonymized.FirstName);
        Assert.Null(anonymized.LastName);
        Assert.Null(anonymized.CompanyName);
        Assert.Null(anonymized.Ico);
        Assert.Null(anonymized.Dic);
        Assert.Null(anonymized.IcDph);
        Assert.Null(anonymized.Phone);
        Assert.Null(anonymized.Note);
        Assert.Null(anonymized.UserId);
        Assert.Equal(string.Empty, anonymized.Address.Street);
        Assert.Equal(string.Empty, anonymized.Address.City);
        Assert.Equal(string.Empty, anonymized.Address.PostalCode);
        Assert.EndsWith("@anonymized.invalid", anonymized.Email, StringComparison.Ordinal);

        var stored = await _factory.ExecuteDbAsync(db => db.Set<Customer>().AsNoTracking().SingleAsync(c => c.Id == customer.Id));
        Assert.Equal(anonymized.Email, stored.Email);
        Assert.Null(stored.UserId);
        Assert.True(await _factory.ExecuteDbAsync(db => db.Users.AnyAsync(u => u.Id == user.Id)));

        var audit = await admin.GetJsonAsync<List<CustomerAuditEntryResponse>>($"/api/customers/{customer.Id}/audit");
        Assert.Equal([CustomerAction.Created, CustomerAction.Anonymized], audit.Select(e => e.Action));
        Assert.DoesNotContain(audit, entry => (entry.Details ?? string.Empty).Contains("Novák", StringComparison.Ordinal));

        using var update = await sales.PutJsonAsync($"/api/customers/{customer.Id}", CustomerTestData.Person());
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        using var reactivate = await admin.PostAsync($"/api/customers/{customer.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.Conflict, reactivate.StatusCode);
        using var again = await admin.PostAsync($"/api/customers/{customer.Id}/anonymize", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task AnonymizedCustomer_KeepsContractsReadableAndPdfGeneratable()
    {
        using var sales = Sales(Guid.CreateVersion7());
        var customer = await ContractTestData.NewCustomerAsync(sales);
        var quote = await ContractTestData.NewQuoteAsync(sales, customer);
        using var created = await ContractTestData.CreateContractAsync(sales, quote.Id);
        var contract = await created.ReadJsonAsync<SolarOps.Api.Features.Contracts.ContractResponse>();

        using var admin = Admin();
        using var anonymized = await admin.PostAsync($"/api/customers/{customer.Id}/anonymize", null);
        anonymized.EnsureSuccessStatusCode();

        using var fetched = await admin.GetAsync($"/api/contracts/{contract.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        using var pdf = await admin.PostAsync($"/api/contracts/{contract.Id}/pdf", null);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(Roles.Sales)]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Operator)]
    [InlineData(Roles.Customer)]
    public async Task Anonymize_AndAudit_OtherRoles_Return403(string role)
    {
        using var sales = Sales(Guid.CreateVersion7());
        var customer = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person());
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        using var anonymize = await client.PostAsync($"/api/customers/{customer.Id}/anonymize", null);
        using var audit = await client.GetAsync($"/api/customers/{customer.Id}/audit");

        Assert.Equal(HttpStatusCode.Forbidden, anonymize.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, audit.StatusCode);
    }

    [Fact]
    public async Task Anonymize_AndAudit_Anonymous_Return401()
    {
        using var sales = Sales(Guid.CreateVersion7());
        var customer = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person());
        using var anonymous = _factory.CreateApiClient();

        using var anonymize = await anonymous.PostAsync($"/api/customers/{customer.Id}/anonymize", null);
        using var audit = await anonymous.GetAsync($"/api/customers/{customer.Id}/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymize.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, audit.StatusCode);
    }

    [Fact]
    public async Task AnonymizeAndAudit_UnknownCustomer_Return404()
    {
        using var admin = Admin();
        var id = Guid.CreateVersion7();

        using var anonymize = await admin.PostAsync($"/api/customers/{id}/anonymize", null);
        using var audit = await admin.GetAsync($"/api/customers/{id}/audit");

        Assert.Equal(HttpStatusCode.NotFound, anonymize.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, audit.StatusCode);
    }

    [Fact]
    public async Task AuditTables_DoNotContainCredentials()
    {
        var columns = await _factory.ExecuteDbAsync(db => db.Database
            .SqlQuery<string>($"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name IN ('customer_audit_entries', 'contract_audit_entries')")
            .ToListAsync());

        Assert.DoesNotContain(columns, column => column.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, column => column.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, column => column.Contains("hash", StringComparison.OrdinalIgnoreCase));
    }
}
