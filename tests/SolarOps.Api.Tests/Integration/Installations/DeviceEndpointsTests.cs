using System.Net;
using System.Text;
using Npgsql;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;

namespace SolarOps.Api.Tests.Integration.Installations;

[Collection(PostgresCollection.Name)]
public sealed class DeviceEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Technician() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Technician);

    private async Task<Guid> NewInstallationAsync()
    {
        using var op = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        return (await op.CreateInstallationAsync()).Installation.Id;
    }

    private static string Url(Guid installationId, string suffix = "") => $"/api/installations/{installationId}/devices{suffix}";

    private static AddDeviceRequest Panel(string serial = "PAN-0001", int power = 450) => new()
    {
        Type = DeviceType.Panel,
        SerialNumber = serial,
        Manufacturer = "JA Solar",
        Model = "JAM54S30",
        PowerW = power,
    };

    private static AddDeviceRequest Inverter(string serial = "INV-0001") => new()
    {
        Type = DeviceType.Inverter,
        SerialNumber = serial,
        Manufacturer = "Fronius",
        Model = "Symo 10.0",
        PowerW = 10000,
    };

    private static ReplaceDeviceRequest Replacement(string serial = "PAN-0002") => new()
    {
        SerialNumber = serial,
        Manufacturer = "Longi",
        Model = "LR5-54",
        PowerW = 460,
        Reason = "Defective cell",
    };

    private static async Task<DeviceResponse> AddAsync(HttpClient client, Guid installationId, AddDeviceRequest request)
    {
        using var response = await client.PostJsonAsync(Url(installationId), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<DeviceResponse>())!;
    }

    private static async Task<DeviceReplacementResponse> ReplaceAsync(HttpClient client, Guid installationId, Guid deviceId, string newSerial)
    {
        using var response = await client.PostJsonAsync(Url(installationId, $"/{deviceId}/replace"), Replacement(newSerial));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<DeviceReplacementResponse>())!;
    }

    // Walks the lifecycle through the domain model, so the test does not depend on the transition endpoints.
    private async Task HandOverAsync(Guid installationId)
    {
        await _factory.ExecuteDbAsync(async db =>
        {
            var installation = (await db.Set<Installation>().FindAsync(installationId))!;
            var by = Guid.CreateVersion7();
            var now = DateTimeOffset.UtcNow;
            installation.Schedule(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3), now);
            installation.AssignTechnicians([Guid.CreateVersion7()], now);
            foreach (var state in new[]
                     {
                         InstallationState.ComponentsOrdered, InstallationState.ComponentsReceived, InstallationState.Mounting,
                         InstallationState.MountingDone, InstallationState.Inspection, InstallationState.InspectionPassed,
                         InstallationState.Commissioned, InstallationState.HandedOver,
                     })
            {
                installation.TransitionTo(state, by, null, now);
            }

            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Add_ReturnsCreatedDevice_WithLocation_AndNormalizedSerial()
    {
        var installationId = await NewInstallationAsync();
        var userId = Guid.CreateVersion7();
        using var client = _factory.CreateApiClient(userId, Roles.Technician);

        using var response = await client.PostJsonAsync(Url(installationId), Panel(" pan-0001 "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var device = (await response.ReadAsync<DeviceResponse>())!;
        Assert.Equal($"{Url(installationId)}/{device.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(installationId, device.InstallationId);
        Assert.Equal(DeviceType.Panel, device.Type);
        Assert.Equal("PAN-0001", device.SerialNumber);
        Assert.Equal("JA Solar", device.Manufacturer);
        Assert.Equal("JAM54S30", device.Model);
        Assert.Equal(450, device.PowerW);
        Assert.Equal(DeviceStatus.Installed, device.Status);
        Assert.Equal(userId, device.RegisteredBy);
    }

    [Fact]
    public async Task Add_DuplicateSerial_Returns409_CaseInsensitive()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        await AddAsync(client, installationId, Panel("PAN-0001"));

        using var response = await client.PostJsonAsync(Url(installationId), Panel("pan-0001"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Add_SerialUsedInAnotherInstallation_Returns409()
    {
        var first = await NewInstallationAsync();
        var second = await NewInstallationAsync();
        using var client = Technician();
        await AddAsync(client, first, Panel("PAN-0001"));

        using var response = await client.PostJsonAsync(Url(second), Inverter("PAN-0001"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"type":"Panel","manufacturer":"M","model":"X","powerW":450}""")]
    [InlineData("""{"type":"Panel","serialNumber":"S1","model":"X","powerW":450}""")]
    [InlineData("""{"type":"Panel","serialNumber":"S1","manufacturer":"M","powerW":450}""")]
    [InlineData("""{"type":"Panel","serialNumber":"S1","manufacturer":"M","model":"X"}""")]
    [InlineData("""{"type":"Panel","serialNumber":"S1","manufacturer":"M","model":"X","powerW":0}""")]
    [InlineData("""{"serialNumber":"S1","manufacturer":"M","model":"X","powerW":450}""")]
    [InlineData("""{"type":"Panel","serialNumber":"  ","manufacturer":"M","model":"X","powerW":450}""")]
    public async Task Add_InvalidBody_Returns400(string json)
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();

        using var response = await client.PostAsync(Url(installationId), new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_UnknownInstallation_Returns404()
    {
        using var client = Technician();

        using var response = await client.PostJsonAsync(Url(Guid.CreateVersion7()), Panel());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Add_ToHandedOverInstallation_Returns409()
    {
        var installationId = await NewInstallationAsync();
        await HandOverAsync(installationId);
        using var client = Technician();

        using var response = await client.PostJsonAsync(Url(installationId), Panel());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsInstalledDevices_ByDefault_AndFiltersByType()
    {
        var installationId = await NewInstallationAsync();
        var otherInstallationId = await NewInstallationAsync();
        using var client = Technician();
        var panel = await AddAsync(client, installationId, Panel("PAN-0001"));
        var inverter = await AddAsync(client, installationId, Inverter());
        await AddAsync(client, otherInstallationId, Panel("PAN-OTHER"));
        await ReplaceAsync(client, installationId, panel.Id, "PAN-0002");

        var current = await client.GetJsonAsync<List<DeviceResponse>>(Url(installationId));
        var all = await client.GetJsonAsync<List<DeviceResponse>>(Url(installationId) + "?includeReplaced=true");
        var inverters = await client.GetJsonAsync<List<DeviceResponse>>(Url(installationId) + "?type=Inverter");

        Assert.Equal(["INV-0001", "PAN-0002"], current.Select(d => d.SerialNumber).Order(StringComparer.Ordinal));
        Assert.All(current, d => Assert.Equal(DeviceStatus.Installed, d.Status));
        Assert.Equal(3, all.Count);
        Assert.Contains(all, d => d.Id == panel.Id && d.Status == DeviceStatus.Replaced);
        Assert.Equal([inverter.Id], inverters.Select(d => d.Id));
    }

    [Fact]
    public async Task List_UnknownType_Returns400()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();

        using var response = await client.GetAsync(Url(installationId) + "?type=Battery");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsDevice_AndUnknownOrForeignDeviceIs404()
    {
        var installationId = await NewInstallationAsync();
        var otherInstallationId = await NewInstallationAsync();
        using var client = Technician();
        var device = await AddAsync(client, installationId, Panel());

        var fetched = await client.GetJsonAsync<DeviceResponse>(Url(installationId, $"/{device.Id}"));
        using var unknown = await client.GetAsync(Url(installationId, $"/{Guid.CreateVersion7()}"));
        using var wrongInstallation = await client.GetAsync(Url(otherInstallationId, $"/{device.Id}"));

        Assert.Equal(device.Id, fetched.Id);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongInstallation.StatusCode);
    }

    [Fact]
    public async Task Replace_KeepsOldDeviceInHistory_AndLinksBoth()
    {
        var installationId = await NewInstallationAsync();
        var userId = Guid.CreateVersion7();
        using var client = _factory.CreateApiClient(userId, Roles.Technician);
        var old = await AddAsync(client, installationId, Panel("PAN-0001"));

        using var response = await client.PostJsonAsync(Url(installationId, $"/{old.Id}/replace"), Replacement("PAN-0002"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.ReadAsync<DeviceReplacementResponse>())!;
        Assert.Equal($"{Url(installationId)}/{body.Replacement.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(old.Id, body.Replaced.Id);
        Assert.Equal(DeviceStatus.Replaced, body.Replaced.Status);
        Assert.Equal(body.Replacement.Id, body.Replaced.ReplacedByDeviceId);
        Assert.Equal("Defective cell", body.Replaced.RemovalReason);
        Assert.Equal(userId, body.Replaced.RemovedBy);
        Assert.NotNull(body.Replaced.RemovedAt);
        Assert.Equal(DeviceStatus.Installed, body.Replacement.Status);
        Assert.Equal(old.Id, body.Replacement.ReplacesDeviceId);
        Assert.Equal(DeviceType.Panel, body.Replacement.Type);
        Assert.Equal("Longi", body.Replacement.Manufacturer);
        Assert.Equal(460, body.Replacement.PowerW);

        var stillThere = await client.GetJsonAsync<DeviceResponse>(Url(installationId, $"/{old.Id}"));
        Assert.Equal("PAN-0001", stillThere.SerialNumber);
    }

    [Fact]
    public async Task History_ReturnsWholeChain_OldestFirst_FromAnyMember()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var first = await AddAsync(client, installationId, Panel("PAN-0001"));
        var second = (await ReplaceAsync(client, installationId, first.Id, "PAN-0002")).Replacement;
        var third = (await ReplaceAsync(client, installationId, second.Id, "PAN-0003")).Replacement;
        await AddAsync(client, installationId, Panel("PAN-UNRELATED"));

        var fromMiddle = await client.GetJsonAsync<List<DeviceResponse>>(Url(installationId, $"/{second.Id}/history"));
        var fromFirst = await client.GetJsonAsync<List<DeviceResponse>>(Url(installationId, $"/{first.Id}/history"));

        var expected = new[] { first.Id, second.Id, third.Id };
        Assert.Equal(expected, fromMiddle.Select(d => d.Id));
        Assert.Equal(expected, fromFirst.Select(d => d.Id));
        Assert.Equal([DeviceStatus.Replaced, DeviceStatus.Replaced, DeviceStatus.Installed], fromMiddle.Select(d => d.Status));
    }

    [Fact]
    public async Task History_OfDeviceNeverReplaced_ContainsOnlyIt()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var device = await AddAsync(client, installationId, Inverter());

        var history = await client.GetJsonAsync<List<DeviceResponse>>(Url(installationId, $"/{device.Id}/history"));

        Assert.Equal([device.Id], history.Select(d => d.Id));
    }

    [Fact]
    public async Task Replace_SerialAlreadyUsed_Returns409_AndKeepsOldInstalled()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var old = await AddAsync(client, installationId, Panel("PAN-0001"));
        await AddAsync(client, installationId, Panel("PAN-0002"));

        using var response = await client.PostJsonAsync(Url(installationId, $"/{old.Id}/replace"), Replacement("pan-0002"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var fetched = await client.GetJsonAsync<DeviceResponse>(Url(installationId, $"/{old.Id}"));
        Assert.Equal(DeviceStatus.Installed, fetched.Status);
    }

    [Fact]
    public async Task Replace_ReusingSerialOfReplacedDevice_Returns409()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var old = await AddAsync(client, installationId, Panel("PAN-0001"));
        var current = (await ReplaceAsync(client, installationId, old.Id, "PAN-0002")).Replacement;

        using var response = await client.PostJsonAsync(Url(installationId, $"/{current.Id}/replace"), Replacement("PAN-0001"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Replace_AlreadyReplacedDevice_Returns409()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var old = await AddAsync(client, installationId, Panel("PAN-0001"));
        await ReplaceAsync(client, installationId, old.Id, "PAN-0002");

        using var response = await client.PostJsonAsync(Url(installationId, $"/{old.Id}/replace"), Replacement("PAN-0003"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Replace_WithoutReason_Returns400()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var old = await AddAsync(client, installationId, Panel());

        using var response = await client.PostJsonAsync(Url(installationId, $"/{old.Id}/replace"), Replacement() with { Reason = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Replace_UnknownDevice_Returns404()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();

        using var response = await client.PostJsonAsync(Url(installationId, $"/{Guid.CreateVersion7()}/replace"), Replacement());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Replace_AfterHandover_IsStillAllowed()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var old = await AddAsync(client, installationId, Panel());
        await HandOverAsync(installationId);

        using var response = await client.PostJsonAsync(Url(installationId, $"/{old.Id}/replace"), Replacement());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Roles_AnonymousIs401_SalesCannotWrite_ButCanRead()
    {
        var installationId = await NewInstallationAsync();
        using var anonymous = _factory.CreateApiClient();
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        using var tech = Technician();
        var device = await AddAsync(tech, installationId, Panel());

        using var anonymousList = await anonymous.GetAsync(Url(installationId));
        using var salesAdd = await sales.PostJsonAsync(Url(installationId), Panel("PAN-X"));
        using var salesReplace = await sales.PostJsonAsync(Url(installationId, $"/{device.Id}/replace"), Replacement());
        using var salesList = await sales.GetAsync(Url(installationId));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, salesAdd.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, salesReplace.StatusCode);
        Assert.Equal(HttpStatusCode.OK, salesList.StatusCode);
    }

    [Fact]
    public async Task Customer_SeesDevicesOfOwnInstallationOnly()
    {
        var customerUser = new AppUser { UserName = $"u-{Guid.NewGuid():N}@example.sk", Email = $"u-{Guid.NewGuid():N}@example.sk" };
        await _factory.ExecuteDbAsync(async db =>
        {
            db.Users.Add(customerUser);
            await db.SaveChangesAsync();
        });
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var own = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("own@example.sk") with { UserId = customerUser.Id });
        var foreign = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("foreign@example.sk", lastName: "Kováč"));

        using var op = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        var ownInstallation = (await op.CreateInstallationAsync(InstallationsTestData.NewInstallationRequest() with { CustomerId = own.Id })).Installation.Id;
        var foreignInstallation = (await op.CreateInstallationAsync(InstallationsTestData.NewInstallationRequest() with { CustomerId = foreign.Id })).Installation.Id;
        using var tech = Technician();
        var ownDevice = await AddAsync(tech, ownInstallation, Panel("PAN-OWN"));
        var foreignDevice = await AddAsync(tech, foreignInstallation, Panel("PAN-FOREIGN"));

        using var customer = _factory.CreateApiClient(customerUser.Id, Roles.Customer);
        var list = await customer.GetJsonAsync<List<DeviceResponse>>(Url(ownInstallation));
        using var foreignList = await customer.GetAsync(Url(foreignInstallation));
        using var foreignGet = await customer.GetAsync(Url(foreignInstallation, $"/{foreignDevice.Id}"));
        using var foreignHistory = await customer.GetAsync(Url(foreignInstallation, $"/{foreignDevice.Id}/history"));
        using var write = await customer.PostJsonAsync(Url(ownInstallation), Panel("PAN-NEW"));

        Assert.Equal([ownDevice.Id], list.Select(d => d.Id));
        Assert.Equal(HttpStatusCode.NotFound, foreignList.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignHistory.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }
}
