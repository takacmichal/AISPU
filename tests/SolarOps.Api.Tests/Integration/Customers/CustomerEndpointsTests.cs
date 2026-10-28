using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Customers;

[Collection(PostgresCollection.Name)]
public sealed class CustomerEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose() => _factory.Dispose();

    private HttpClient Sales() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

    [Fact]
    public async Task Create_Person_Returns201WithLocation()
    {
        using var client = Sales();

        using var response = await client.PostJsonAsync("/api/customers", CustomerTestData.Person());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var customer = await response.ReadJsonAsync<CustomerResponse>();
        Assert.Equal($"/api/customers/{customer.Id}", response.Headers.Location?.ToString());
        Assert.Equal(CustomerType.Person, customer.Type);
        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.Equal("Ján Novák", customer.DisplayName);

        var fetched = await client.GetJsonAsync<CustomerResponse>(response.Headers.Location!.ToString());
        Assert.Equal(customer.Id, fetched.Id);
        Assert.Equal("jan.novak@example.sk", fetched.Email);
    }

    [Fact]
    public async Task Create_Company_StoresIdentifiers()
    {
        using var client = Sales();
        var request = CustomerTestData.Company() with { Ico = "357 634 69", IcDph = "sk2020273893" };

        var customer = await CustomerTestData.CreateAsync(client, request);

        Assert.Equal("35763469", customer.Ico);
        Assert.Equal("SK2020273893", customer.IcDph);
        Assert.Null(customer.FirstName);
    }

    [Theory]
    [InlineData("Email", "not-an-email", null)]
    [InlineData("Ico", "info@firma.sk", "")]
    [InlineData("Ico", "info@firma.sk", "35763468")]
    public async Task Create_InvalidEmailOrIco_Returns400(string field, string email, string? ico)
    {
        using var client = Sales();
        var request = CustomerTestData.Company(email) with { Ico = ico ?? CustomerTestData.ValidIco };

        using var response = await client.PostJsonAsync("/api/customers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains(field, problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_MissingRequiredFields_Returns400()
    {
        using var client = Sales();

        using var response = await client.PostJsonAsync("/api/customers", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("Type", problem.Errors.Keys);
        Assert.Contains("Email", problem.Errors.Keys);
        Assert.Contains("Address", problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_DuplicateEmail_Returns409_CaseInsensitive()
    {
        using var client = Sales();
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("jan.novak@example.sk"));

        using var response = await client.PostJsonAsync("/api/customers", CustomerTestData.Company(" JAN.Novak@example.SK "));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_WithLinkedUser_ValidatesUserExistsAndIsLinkedOnce()
    {
        using var client = Sales();
        var user = new AppUser { UserName = "zakaznik@example.sk", Email = "zakaznik@example.sk" };
        await _factory.ExecuteDbAsync(async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });

        using var unknownUser = await client.PostJsonAsync("/api/customers", CustomerTestData.Person() with { UserId = Guid.CreateVersion7() });
        Assert.Equal(HttpStatusCode.BadRequest, unknownUser.StatusCode);

        var linked = await CustomerTestData.CreateAsync(client, CustomerTestData.Person() with { UserId = user.Id });
        Assert.Equal(user.Id, linked.UserId);

        using var secondLink = await client.PostJsonAsync("/api/customers", CustomerTestData.Person("iny@example.sk") with { UserId = user.Id });
        Assert.Equal(HttpStatusCode.Conflict, secondLink.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesData_AndRejectsEmailOfAnotherCustomer()
    {
        using var client = Sales();
        var customer = await CustomerTestData.CreateAsync(client, CustomerTestData.Person());
        await CustomerTestData.CreateAsync(client, CustomerTestData.Company("info@slnko.sk"));

        var update = new UpdateCustomerRequest
        {
            Type = CustomerType.Person,
            FirstName = "Ján",
            LastName = "Nový",
            Email = "jan.novak@example.sk",
            Address = CustomerTestData.Address() with { City = "Trnava", PostalCode = "917 01" },
        };
        using var ok = await client.PutJsonAsync($"/api/customers/{customer.Id}", update);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var updated = await ok.ReadJsonAsync<CustomerResponse>();
        Assert.Equal("Nový", updated.LastName);
        Assert.Equal("Trnava", updated.Address.City);

        using var conflict = await client.PutJsonAsync($"/api/customers/{customer.Id}", update with { Email = "info@slnko.sk" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var missing = await client.PutJsonAsync($"/api/customers/{Guid.CreateVersion7()}", update);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Archive_ThenEditOrArchiveAgain_Returns409()
    {
        using var client = Sales();
        var customer = await CustomerTestData.CreateAsync(client, CustomerTestData.Person());

        using var archived = await client.PostAsync($"/api/customers/{customer.Id}/archive", null);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(CustomerStatus.Archived, (await archived.ReadJsonAsync<CustomerResponse>()).Status);

        using var again = await client.PostAsync($"/api/customers/{customer.Id}/archive", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        using var reactivated = await client.PostAsync($"/api/customers/{customer.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        using var client = Sales();

        using var response = await client.GetAsync($"/api/customers/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.PostJsonAsync("/api/customers", CustomerTestData.Person());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Customer)]
    public async Task Create_WithoutSalesOrAdminRole_Returns403(string role)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        using var response = await client.PostJsonAsync("/api/customers", CustomerTestData.Person());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateCustomer()
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);

        using var response = await client.PostJsonAsync("/api/customers", CustomerTestData.Person());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task List_NoCustomers_Returns200WithEmptyItems()
    {
        using var client = Sales();

        var list = await client.GetJsonAsync<CustomerListResponse>("/api/customers");

        Assert.Empty(list.Items);
        Assert.Equal(0, list.TotalCount);
        Assert.Equal(1, list.Page);
        Assert.Equal(20, list.PageSize);
    }

    [Fact]
    public async Task List_DefaultsToSurnameOrder_AndPages()
    {
        using var client = Sales();
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("c@example.sk", lastName: "Zelený"));
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("a@example.sk", lastName: "Adamec"));
        await CustomerTestData.CreateAsync(client, CustomerTestData.Company("b@example.sk", companyName: "Bratia s.r.o."));

        var all = await client.GetJsonAsync<CustomerListResponse>("/api/customers");
        var second = await client.GetJsonAsync<CustomerListResponse>("/api/customers?page=2&pageSize=2");

        Assert.Equal(["a@example.sk", "b@example.sk", "c@example.sk"], all.Items.Select(c => c.Email));
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(["c@example.sk"], second.Items.Select(c => c.Email));
    }

    [Fact]
    public async Task List_SortsByRequestedColumnAndDirection()
    {
        using var client = Sales();
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("c@example.sk", lastName: "Zelený"));
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("a@example.sk", lastName: "Adamec"));
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("b@example.sk", lastName: "Mráz"));

        var byNameDesc = await client.GetJsonAsync<CustomerListResponse>("/api/customers?sortBy=name&sortDir=desc");
        var byEmail = await client.GetJsonAsync<CustomerListResponse>("/api/customers?sortBy=email");

        Assert.Equal(["c@example.sk", "b@example.sk", "a@example.sk"], byNameDesc.Items.Select(c => c.Email));
        Assert.Equal(["a@example.sk", "b@example.sk", "c@example.sk"], byEmail.Items.Select(c => c.Email));
    }

    [Theory]
    [InlineData("sortBy=nope")]
    [InlineData("sortDir=sideways")]
    public async Task List_RejectsUnknownSort(string query)
    {
        using var client = Sales();

        using var response = await client.GetAsync($"/api/customers?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("adam", "a@example.sk")]
    [InlineData("BRATIA", "b@example.sk")]
    [InlineData("c@example", "c@example.sk")]
    [InlineData("35763469", "b@example.sk")]
    public async Task List_Query_MatchesCaseInsensitively(string q, string expectedEmail)
    {
        using var client = Sales();
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("a@example.sk", lastName: "Adamec"));
        await CustomerTestData.CreateAsync(client, CustomerTestData.Company("b@example.sk", companyName: "Bratia s.r.o."));
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("c@example.sk", lastName: "Zelený"));

        var list = await client.GetJsonAsync<CustomerListResponse>($"/api/customers?q={Uri.EscapeDataString(q)}");

        Assert.Equal([expectedEmail], list.Items.Select(c => c.Email));
    }

    [Fact]
    public async Task List_QueryWithLikeWildcards_IsTreatedLiterally()
    {
        using var client = Sales();
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("a@example.sk"));

        var list = await client.GetJsonAsync<CustomerListResponse>("/api/customers?q=%25");

        Assert.Empty(list.Items);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_InvalidPaging_Returns400(string query)
    {
        using var client = Sales();

        using var response = await client.GetAsync($"/api/customers?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Schema_HasUniqueEmailIndex()
    {
        var indexes = await _factory.ExecuteDbAsync(db => db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'customers'")
            .ToListAsync());

        Assert.True(indexes.Any(i => i.Contains("UNIQUE", StringComparison.Ordinal) && i.Contains("(email)", StringComparison.Ordinal)), string.Join(" | ", indexes));
    }
}
