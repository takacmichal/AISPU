using System.Net;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Installations;

[Collection(PostgresCollection.Name)]
public sealed class InstallationEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose() => _factory.Dispose();

    private HttpClient Operator() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    [Fact]
    public async Task Create_ReturnsCreatedPlannedInstallation_WithLocation()
    {
        using var client = Operator();
        var request = InstallationsTestData.NewInstallationRequest();

        var response = await client.PostJsonAsync("/api/installations", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadAsync<InstallationDetailResponse>();
        Assert.NotNull(body);
        Assert.Equal(InstallationState.Planned, body.Installation.State);
        Assert.Equal(request.ContractId, body.Installation.ContractId);
        Assert.Equal(request.SupplyPointId, body.Installation.SupplyPointId);
        Assert.Equal($"/api/installations/{body.Installation.Id}", response.Headers.Location?.OriginalString);
        Assert.Single(body.StateHistory);
    }

    [Fact]
    public async Task Create_WithoutContractOrSupplyPoint_Returns400()
    {
        using var client = Operator();

        var response = await client.PostJsonAsync("/api/installations", new { customerId = Guid.CreateVersion7(), name = "X" });
        var emptyId = await client.PostJsonAsync("/api/installations", InstallationsTestData.NewInstallationRequest() with { SupplyPointId = Guid.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ContractId", body);
        Assert.Contains("SupplyPointId", body);
        Assert.Equal(HttpStatusCode.BadRequest, emptyId.StatusCode);
        Assert.Contains("SupplyPointId", await emptyId.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_SecondInstallationForSameContract_Returns409()
    {
        using var client = Operator();
        var request = InstallationsTestData.NewInstallationRequest();
        await client.CreateInstallationAsync(request);

        var response = await client.PostJsonAsync("/api/installations", request with { SupplyPointId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication_AndRole()
    {
        using var anonymous = _factory.CreateApiClient();
        using var customer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);
        using var technician = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Technician);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/installations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/installations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await technician.GetAsync("/api/installations")).StatusCode);
        // Technicians do field work but do not plan installations.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await technician.PostJsonAsync("/api/installations", InstallationsTestData.NewInstallationRequest())).StatusCode);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        using var client = Operator();

        var response = await client.GetAsync($"/api/installations/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByState_AndRejectsUnknownState()
    {
        using var client = Operator();
        var planned = await client.CreateInstallationAsync();
        var ordered = await client.CreateInstallationAsync();
        (await client.PostJsonAsync($"/api/installations/{ordered.Installation.Id}/transitions",
            new InstallationTransitionRequest { TargetState = InstallationState.ComponentsOrdered })).EnsureSuccessStatusCode();

        var plannedList = await (await client.GetAsync("/api/installations?state=Planned")).ReadAsync<List<InstallationResponse>>();
        var orderedList = await (await client.GetAsync("/api/installations?state=componentsordered")).ReadAsync<List<InstallationResponse>>();
        var all = await (await client.GetAsync("/api/installations")).ReadAsync<List<InstallationResponse>>();
        var bad = await client.GetAsync("/api/installations?state=Nonsense");

        Assert.Equal([planned.Installation.Id], plannedList!.Select(x => x.Id));
        Assert.Equal([ordered.Installation.Id], orderedList!.Select(x => x.Id));
        Assert.Equal(2, all!.Count);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByContractAndSupplyPoint()
    {
        using var client = Operator();
        var first = await client.CreateInstallationAsync();
        await client.CreateInstallationAsync();

        var byContract = await (await client.GetAsync($"/api/installations?contractId={first.Installation.ContractId}"))
            .ReadAsync<List<InstallationResponse>>();
        var bySupplyPoint = await (await client.GetAsync($"/api/installations?supplyPointId={first.Installation.SupplyPointId}"))
            .ReadAsync<List<InstallationResponse>>();

        Assert.Equal([first.Installation.Id], byContract!.Select(x => x.Id));
        Assert.Equal([first.Installation.Id], bySupplyPoint!.Select(x => x.Id));
    }

    [Fact]
    public async Task Transition_Invalid_Returns409_AndValidPathIsRecorded()
    {
        using var client = Operator();
        var created = await client.CreateInstallationAsync();
        var url = $"/api/installations/{created.Installation.Id}/transitions";

        var invalid = await client.PostJsonAsync(url, new InstallationTransitionRequest { TargetState = InstallationState.Commissioned });
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);

        var valid = await client.PostJsonAsync(url, new InstallationTransitionRequest { TargetState = InstallationState.ComponentsOrdered, Note = "Objednané u dodávateľa" });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);

        var detail = await (await client.GetAsync($"/api/installations/{created.Installation.Id}")).ReadAsync<InstallationDetailResponse>();
        Assert.Equal(InstallationState.ComponentsOrdered, detail!.Installation.State);
        Assert.Equal([InstallationState.Planned, InstallationState.ComponentsOrdered], detail.StateHistory.Select(x => x.ToState));
        Assert.Equal("Objednané u dodávateľa", detail.StateHistory[^1].Note);
    }

    [Fact]
    public async Task Transition_UnknownTargetState_Returns400()
    {
        using var client = Operator();
        var created = await client.CreateInstallationAsync();

        var missing = await client.PostJsonAsync($"/api/installations/{created.Installation.Id}/transitions", new { note = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task ScheduleAndTechnicians_EnableMounting()
    {
        using var client = Operator();
        var technicianId = await _factory.SeedTechnicianAsync();
        var created = await client.CreateInstallationAsync();
        var id = created.Installation.Id;

        foreach (var state in new[] { InstallationState.ComponentsOrdered, InstallationState.ComponentsReceived })
        {
            (await client.PostJsonAsync($"/api/installations/{id}/transitions", new InstallationTransitionRequest { TargetState = state })).EnsureSuccessStatusCode();
        }

        var tooEarly = await client.PostJsonAsync($"/api/installations/{id}/transitions", new InstallationTransitionRequest { TargetState = InstallationState.Mounting });
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);

        var badSchedule = await client.PutJsonAsync($"/api/installations/{id}/schedule",
            new ScheduleInstallationRequest { ScheduledStart = new DateOnly(2026, 11, 5), ScheduledEnd = new DateOnly(2026, 11, 4) });
        Assert.Equal(HttpStatusCode.BadRequest, badSchedule.StatusCode);

        (await client.PutJsonAsync($"/api/installations/{id}/schedule",
            new ScheduleInstallationRequest { ScheduledStart = new DateOnly(2026, 11, 4), ScheduledEnd = new DateOnly(2026, 11, 5) })).EnsureSuccessStatusCode();

        var notTechnician = await client.PutJsonAsync($"/api/installations/{id}/technicians",
            new AssignTechniciansRequest { TechnicianIds = [Guid.CreateVersion7()] });
        Assert.Equal(HttpStatusCode.BadRequest, notTechnician.StatusCode);

        var assigned = await client.PutJsonAsync($"/api/installations/{id}/technicians",
            new AssignTechniciansRequest { TechnicianIds = [technicianId] });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        using var technician = _factory.CreateApiClient(technicianId, Roles.Technician);
        var mounting = await technician.PostJsonAsync($"/api/installations/{id}/transitions", new InstallationTransitionRequest { TargetState = InstallationState.Mounting });
        Assert.Equal(HttpStatusCode.OK, mounting.StatusCode);

        var mine = await (await technician.GetAsync("/api/installations?assignedToMe=true")).ReadAsync<List<InstallationResponse>>();
        var detail = Assert.Single(mine!);
        Assert.Equal(InstallationState.Mounting, detail.State);
        Assert.Equal(new DateOnly(2026, 11, 4), detail.ScheduledStart);
        Assert.Equal([technicianId], detail.TechnicianIds);
    }

    [Fact]
    public async Task Update_ChangesDetails_And404ForUnknown()
    {
        using var client = Operator();
        var created = await client.CreateInstallationAsync();

        var updated = await client.PutJsonAsync($"/api/installations/{created.Installation.Id}",
            new UpdateInstallationRequest { Name = "FVE Nitra – garáž", PlannedPowerKwp = 12.3m, Note = "Strecha z plechu" });
        var missing = await client.PutJsonAsync($"/api/installations/{Guid.CreateVersion7()}", new UpdateInstallationRequest { Name = "X" });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.ReadAsync<InstallationDetailResponse>();
        Assert.Equal("FVE Nitra – garáž", body!.Installation.Name);
        Assert.Equal(12.3m, body.Installation.PlannedPowerKwp);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
