using System.Net;
using Npgsql;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;
using Data = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.ConnectionRequests;

[Collection(PostgresCollection.Name)]
public sealed class ConnectionRequestEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

    private static CreateConnectionRequestRequest NewRequest(Guid installationId, decimal acPowerKw = 8m) => new()
    {
        InstallationId = installationId,
        EicCode = "24ZZS12345678901",
        RatedAcPowerKw = acPowerKw,
        MainBreakerAmps = 25,
        Attachments =
        [
            new AttachmentRequest { Kind = ConnectionRequestAttachmentKind.SingleLineDiagram, FileName = "schema.pdf", ContentType = "application/pdf", SizeBytes = 120_000 },
            new AttachmentRequest { Kind = ConnectionRequestAttachmentKind.OwnerConsent, FileName = "suhlas.pdf", ContentType = "application/pdf", SizeBytes = 80_000 },
        ],
    };

    private static async Task<InstallationResponse> NewInstallationAsync(HttpClient client, Guid? customerId = null)
    {
        var request = Data.NewInstallationRequest();
        if (customerId is not null)
        {
            request = request with { CustomerId = customerId };
        }

        return (await Data.CreateInstallationAsync(client, request)).Installation;
    }

    [Fact]
    public async Task Create_ReturnsCreatedDraft_LinkedToInstallation_WithAttachments()
    {
        using var client = Sales();
        var installation = await NewInstallationAsync(client);

        var response = await client.PostJsonAsync("/api/connection-requests", NewRequest(installation.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadJsonAsync<ConnectionRequestResponse>();
        Assert.NotNull(body);
        Assert.Equal(ConnectionRequestState.Draft, body.State);
        Assert.Equal(ConnectionRequestType.SmallSource, body.Type);
        Assert.Equal(installation.Id, body.InstallationId);
        Assert.Equal(installation.CustomerId, body.CustomerId);
        Assert.Equal(installation.SupplyPointId, body.SupplyPointId);
        Assert.Equal(2, body.Attachments.Count);
        Assert.Equal($"/api/connection-requests/{body.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Create_AbovePowerThreshold_IsLargeSource()
    {
        using var client = Sales();
        var installation = await NewInstallationAsync(client);

        var response = await client.PostJsonAsync("/api/connection-requests", NewRequest(installation.Id, 12m));

        var body = await response.ReadJsonAsync<ConnectionRequestResponse>();
        Assert.Equal(ConnectionRequestType.LargeSource, body!.Type);
    }

    [Fact]
    public async Task Create_WithoutRequiredAttachment_Returns400()
    {
        using var client = Sales();
        var installation = await NewInstallationAsync(client);
        var request = NewRequest(installation.Id);

        var noConsent = await client.PostJsonAsync("/api/connection-requests",
            request with { Attachments = [request.Attachments![0]] });
        var none = await client.PostJsonAsync("/api/connection-requests", request with { Attachments = [] });

        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
        Assert.Contains("OwnerConsent", await noConsent.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidInput_Returns400()
    {
        using var client = Sales();
        var installation = await NewInstallationAsync(client);
        var request = NewRequest(installation.Id);

        var noInstallation = await client.PostJsonAsync("/api/connection-requests", request with { InstallationId = null });
        var badPower = await client.PostJsonAsync("/api/connection-requests", request with { RatedAcPowerKw = 0 });
        var noEic = await client.PostJsonAsync("/api/connection-requests", request with { EicCode = " " });
        var emptyFile = await client.PostJsonAsync("/api/connection-requests",
            request with { Attachments = [request.Attachments![0] with { SizeBytes = 0 }, request.Attachments![1]] });

        Assert.Equal(HttpStatusCode.BadRequest, noInstallation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badPower.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noEic.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyFile.StatusCode);
    }

    [Fact]
    public async Task Create_ForUnknownInstallation_Returns400()
    {
        using var client = Sales();

        var response = await client.PostJsonAsync("/api/connection-requests", NewRequest(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("InstallationId", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_ReturnsRequest_AndMissingIs404()
    {
        using var client = Sales();
        var installation = await NewInstallationAsync(client);
        var created = await (await client.PostJsonAsync("/api/connection-requests", NewRequest(installation.Id))).ReadJsonAsync<ConnectionRequestResponse>();

        var found = await client.GetAsync($"/api/connection-requests/{created!.Id}");
        var missing = await client.GetAsync($"/api/connection-requests/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var body = await found.ReadJsonAsync<ConnectionRequestResponse>();
        Assert.Equal(created.Id, body!.Id);
        Assert.Equal(2, body.Attachments.Count);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication_AndRole()
    {
        using var anonymous = _factory.CreateApiClient();
        using var technician = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Technician);
        using var customer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);
        var request = NewRequest(Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/connection-requests/{Guid.CreateVersion7()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostJsonAsync("/api/connection-requests", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await technician.PostJsonAsync("/api/connection-requests", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostJsonAsync("/api/connection-requests", request)).StatusCode);
    }

    [Fact]
    public async Task Get_Customer_SeesOnlyOwnRequest()
    {
        var userA = new AppUser { UserName = $"u-{Guid.NewGuid():N}@example.sk", Email = $"u-{Guid.NewGuid():N}@example.sk" };
        await _factory.ExecuteDbAsync(async db =>
        {
            db.Users.Add(userA);
            await db.SaveChangesAsync();
        });
        using var sales = Sales();
        var customerA = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("a@example.sk") with { UserId = userA.Id });
        var own = await NewInstallationAsync(sales, customerA.Id);
        var foreign = await NewInstallationAsync(sales);
        var ownRequest = await (await sales.PostJsonAsync("/api/connection-requests", NewRequest(own.Id))).ReadJsonAsync<ConnectionRequestResponse>();
        var foreignRequest = await (await sales.PostJsonAsync("/api/connection-requests", NewRequest(foreign.Id))).ReadJsonAsync<ConnectionRequestResponse>();
        using var customer = _factory.CreateApiClient(userA.Id, Roles.Customer);

        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/api/connection-requests/{ownRequest!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/connection-requests/{foreignRequest!.Id}")).StatusCode);
    }
}
