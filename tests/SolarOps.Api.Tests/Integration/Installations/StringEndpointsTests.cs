using System.Net;
using Npgsql;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;

namespace SolarOps.Api.Tests.Integration.Installations;

[Collection(PostgresCollection.Name)]
public sealed class StringEndpointsTests(PostgresFixture postgres) : IDisposable
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

    private static string Url(Guid installationId, string suffix = "") => $"/api/installations/{installationId}/strings{suffix}";

    private static string DevicesUrl(Guid installationId, string suffix = "") => $"/api/installations/{installationId}/devices{suffix}";

    private static CreateStringRequest NewString(int number = 1, int? mppt = 1) => new() { Number = number, Name = $"String {number}", MpptInput = mppt };

    private static async Task<PanelStringResponse> CreateStringAsync(HttpClient client, Guid installationId, CreateStringRequest request)
    {
        using var response = await client.PostJsonAsync(Url(installationId), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<PanelStringResponse>())!;
    }

    private static async Task<DeviceResponse> AddPanelAsync(HttpClient client, Guid installationId, string serial, int power = 450)
    {
        using var response = await client.PostJsonAsync(DevicesUrl(installationId), new AddDeviceRequest
        {
            Type = DeviceType.Panel,
            SerialNumber = serial,
            Manufacturer = "JA Solar",
            Model = "JAM54S30",
            PowerW = power,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<DeviceResponse>())!;
    }

    private static async Task AssignAsync(HttpClient client, Guid installationId, Guid stringId, Guid deviceId)
    {
        using var response = await client.PutAsync(Url(installationId, $"/{stringId}/panels/{deviceId}"), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsCreatedString_WithLocation_AndEmptyTotals()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();

        using var response = await client.PostJsonAsync(Url(installationId), NewString(2, 3));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.ReadAsync<PanelStringResponse>())!;
        Assert.Equal($"{Url(installationId)}/{created.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(2, created.Number);
        Assert.Equal(3, created.MpptInput);
        Assert.Equal(0, created.PanelCount);
        Assert.Equal(0, created.TotalPowerW);
    }

    [Fact]
    public async Task Create_DuplicateNumber_Returns409_ButSameNumberInOtherInstallationIsFine()
    {
        var first = await NewInstallationAsync();
        var second = await NewInstallationAsync();
        using var client = Technician();
        await CreateStringAsync(client, first, NewString(1));

        using var duplicate = await client.PostJsonAsync(Url(first), NewString(1));
        using var other = await client.PostJsonAsync(Url(second), NewString(1));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 25)]
    public async Task Create_OutOfRange_Returns400(int number, int mppt)
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();

        using var response = await client.PostJsonAsync(Url(installationId), NewString(number, mppt));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_MissingNumber_Returns400()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();

        using var response = await client.PostJsonAsync(Url(installationId), new CreateStringRequest { Name = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownInstallation_Returns404()
    {
        using var client = Technician();

        using var response = await client.PostJsonAsync(Url(Guid.CreateVersion7()), NewString());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesFields_AndRejectsNumberOfAnotherString()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        await CreateStringAsync(client, installationId, NewString(1));
        var second = await CreateStringAsync(client, installationId, NewString(2));

        using var ok = await client.PutJsonAsync(Url(installationId, $"/{second.Id}"), new UpdateStringRequest { Number = 2, Name = "East", MpptInput = 2 });
        using var clash = await client.PutJsonAsync(Url(installationId, $"/{second.Id}"), new UpdateStringRequest { Number = 1 });
        using var missing = await client.PutJsonAsync(Url(installationId, $"/{Guid.CreateVersion7()}"), new UpdateStringRequest { Number = 5 });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("East", (await ok.ReadAsync<PanelStringResponse>())!.Name);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Layout_SumsPanelPowerPerString_AndListsUnassigned()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var s1 = await CreateStringAsync(client, installationId, NewString(1, 1));
        var s2 = await CreateStringAsync(client, installationId, NewString(2, 2));
        var a = await AddPanelAsync(client, installationId, "P-A", 450);
        var b = await AddPanelAsync(client, installationId, "P-B", 450);
        var c = await AddPanelAsync(client, installationId, "P-C", 400);
        await AddPanelAsync(client, installationId, "P-D", 300);
        await AssignAsync(client, installationId, s1.Id, a.Id);
        await AssignAsync(client, installationId, s1.Id, b.Id);
        await AssignAsync(client, installationId, s2.Id, c.Id);

        var layout = await client.GetJsonAsync<StringLayoutResponse>(Url(installationId));

        Assert.Equal([1, 2], layout.Strings.Select(s => s.Number));
        Assert.Equal(900, layout.Strings[0].TotalPowerW);
        Assert.Equal(2, layout.Strings[0].PanelCount);
        Assert.Equal(400, layout.Strings[1].TotalPowerW);
        Assert.Equal(["P-D"], layout.UnassignedPanels.Select(p => p.SerialNumber));
        Assert.Equal(1300, layout.AssignedPowerW);
        Assert.Equal(1600, layout.TotalPanelPowerW);
    }

    [Fact]
    public async Task Assign_MovesPanelBetweenStrings_SoItBelongsToExactlyOne()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var s1 = await CreateStringAsync(client, installationId, NewString(1));
        var s2 = await CreateStringAsync(client, installationId, NewString(2));
        var panel = await AddPanelAsync(client, installationId, "P-A");
        await AssignAsync(client, installationId, s1.Id, panel.Id);
        await AssignAsync(client, installationId, s2.Id, panel.Id);

        var first = await client.GetJsonAsync<PanelStringResponse>(Url(installationId, $"/{s1.Id}"));
        var second = await client.GetJsonAsync<PanelStringResponse>(Url(installationId, $"/{s2.Id}"));
        var device = await client.GetJsonAsync<DeviceResponse>(DevicesUrl(installationId, $"/{panel.Id}"));

        Assert.Empty(first.Panels);
        Assert.Equal(0, first.TotalPowerW);
        Assert.Equal([panel.Id], second.Panels.Select(p => p.Id));
        Assert.Equal(s2.Id, device.StringId);
    }

    [Fact]
    public async Task Assign_Inverter_Returns409_UnknownPanel_Returns404()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var s = await CreateStringAsync(client, installationId, NewString());
        using var inverterResponse = await client.PostJsonAsync(DevicesUrl(installationId), new AddDeviceRequest
        {
            Type = DeviceType.Inverter, SerialNumber = "INV-1", Manufacturer = "Fronius", Model = "Symo", PowerW = 10000,
        });
        var inverter = (await inverterResponse.ReadAsync<DeviceResponse>())!;

        using var inverterAssign = await client.PutAsync(Url(installationId, $"/{s.Id}/panels/{inverter.Id}"), null);
        using var unknown = await client.PutAsync(Url(installationId, $"/{s.Id}/panels/{Guid.CreateVersion7()}"), null);

        Assert.Equal(HttpStatusCode.NotFound, inverterAssign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Assign_PanelOfAnotherInstallation_Returns404()
    {
        var first = await NewInstallationAsync();
        var second = await NewInstallationAsync();
        using var client = Technician();
        var s = await CreateStringAsync(client, first, NewString());
        var foreignPanel = await AddPanelAsync(client, second, "P-FOREIGN");

        using var response = await client.PutAsync(Url(first, $"/{s.Id}/panels/{foreignPanel.Id}"), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Assign_ReplacedPanel_Returns409()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var s = await CreateStringAsync(client, installationId, NewString());
        var old = await AddPanelAsync(client, installationId, "P-OLD");
        using var replace = await client.PostJsonAsync(DevicesUrl(installationId, $"/{old.Id}/replace"), new ReplaceDeviceRequest
        {
            SerialNumber = "P-NEW", Manufacturer = "Longi", Model = "LR5", PowerW = 460, Reason = "Defect",
        });
        Assert.Equal(HttpStatusCode.Created, replace.StatusCode);

        using var response = await client.PutAsync(Url(installationId, $"/{s.Id}/panels/{old.Id}"), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReplacingAssignedPanel_KeepsPlaceInStringWithNewPower()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var s = await CreateStringAsync(client, installationId, NewString());
        var old = await AddPanelAsync(client, installationId, "P-OLD", 450);
        await AssignAsync(client, installationId, s.Id, old.Id);

        using var replace = await client.PostJsonAsync(DevicesUrl(installationId, $"/{old.Id}/replace"), new ReplaceDeviceRequest
        {
            SerialNumber = "P-NEW", Manufacturer = "Longi", Model = "LR5", PowerW = 460, Reason = "Defect",
        });
        var result = (await replace.ReadAsync<DeviceReplacementResponse>())!;
        var panelString = await client.GetJsonAsync<PanelStringResponse>(Url(installationId, $"/{s.Id}"));

        Assert.Null(result.Replaced.StringId);
        Assert.Equal(s.Id, result.Replacement.StringId);
        Assert.Equal([result.Replacement.Id], panelString.Panels.Select(p => p.Id));
        Assert.Equal(460, panelString.TotalPowerW);
    }

    [Fact]
    public async Task Unassign_RemovesPanel_AndReturns404WhenNotInThatString()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var s1 = await CreateStringAsync(client, installationId, NewString(1));
        var s2 = await CreateStringAsync(client, installationId, NewString(2));
        var panel = await AddPanelAsync(client, installationId, "P-A");
        await AssignAsync(client, installationId, s1.Id, panel.Id);

        using var wrongString = await client.DeleteAsync(Url(installationId, $"/{s2.Id}/panels/{panel.Id}"));
        using var ok = await client.DeleteAsync(Url(installationId, $"/{s1.Id}/panels/{panel.Id}"));
        var layout = await client.GetJsonAsync<StringLayoutResponse>(Url(installationId));

        Assert.Equal(HttpStatusCode.NotFound, wrongString.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal([panel.Id], layout.UnassignedPanels.Select(p => p.Id));
        Assert.Equal(0, layout.AssignedPowerW);
    }

    [Fact]
    public async Task Delete_EmptyString_Returns204_StringWithPanels_Returns409()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var empty = await CreateStringAsync(client, installationId, NewString(1));
        var full = await CreateStringAsync(client, installationId, NewString(2));
        var panel = await AddPanelAsync(client, installationId, "P-A");
        await AssignAsync(client, installationId, full.Id, panel.Id);

        using var deleted = await client.DeleteAsync(Url(installationId, $"/{empty.Id}"));
        using var blocked = await client.DeleteAsync(Url(installationId, $"/{full.Id}"));
        using var missing = await client.DeleteAsync(Url(installationId, $"/{empty.Id}"));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Layout_UnknownInstallation_Returns404()
    {
        using var client = Technician();

        using var response = await client.GetAsync(Url(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Write_AfterHandover_Returns409()
    {
        var installationId = await NewInstallationAsync();
        using var client = Technician();
        var s = await CreateStringAsync(client, installationId, NewString());
        await _factory.ExecuteDbAsync(async db =>
        {
            var installation = (await db.Set<Installation>().FindAsync(installationId))!;
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
                installation.TransitionTo(state, Guid.CreateVersion7(), null, now);
            }

            await db.SaveChangesAsync();
        });

        using var create = await client.PostJsonAsync(Url(installationId), NewString(2));
        using var delete = await client.DeleteAsync(Url(installationId, $"/{s.Id}"));
        using var read = await client.GetAsync(Url(installationId));

        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task Roles_AnonymousIs401_SalesCannotWrite_ButCanRead()
    {
        var installationId = await NewInstallationAsync();
        using var anonymous = _factory.CreateApiClient();
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        using var tech = Technician();
        var s = await CreateStringAsync(tech, installationId, NewString());

        using var anonymousRead = await anonymous.GetAsync(Url(installationId));
        using var anonymousWrite = await anonymous.PostJsonAsync(Url(installationId), NewString(2));
        using var salesCreate = await sales.PostJsonAsync(Url(installationId), NewString(2));
        using var salesDelete = await sales.DeleteAsync(Url(installationId, $"/{s.Id}"));
        using var salesAssign = await sales.PutAsync(Url(installationId, $"/{s.Id}/panels/{Guid.CreateVersion7()}"), null);
        using var salesRead = await sales.GetAsync(Url(installationId));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, salesCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, salesDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, salesAssign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, salesRead.StatusCode);
    }

    [Fact]
    public async Task Customer_SeesLayoutOfOwnInstallationOnly()
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
        var ownString = await CreateStringAsync(tech, ownInstallation, NewString());
        var foreignString = await CreateStringAsync(tech, foreignInstallation, NewString());

        using var customer = _factory.CreateApiClient(customerUser.Id, Roles.Customer);
        var layout = await customer.GetJsonAsync<StringLayoutResponse>(Url(ownInstallation));
        using var foreignLayout = await customer.GetAsync(Url(foreignInstallation));
        using var foreignGet = await customer.GetAsync(Url(foreignInstallation, $"/{foreignString.Id}"));
        using var write = await customer.PostJsonAsync(Url(ownInstallation), NewString(2));

        Assert.Equal([ownString.Id], layout.Strings.Select(s => s.Id));
        Assert.Equal(HttpStatusCode.NotFound, foreignLayout.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }
}
