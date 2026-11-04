using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using static SolarOps.Api.Tests.Integration.Tickets.VisitTestApi;
using InstallationData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.Tickets;

/// <summary>Intervention (#127), material and device replacement (#128), closing and customer confirmation (#129).</summary>
[Collection(PostgresCollection.Name)]
public sealed class VisitInterventionTests : IDisposable
{
    private readonly FixedTimeProvider _clock = new(Clock);
    private readonly TicketsFactory _factory;

    public VisitInterventionTests(PostgresFixture postgres) => _factory = new TicketsFactory(postgres, _clock);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient As(string role, Guid? userId = null) => _factory.CreateApiClient(userId ?? Guid.CreateVersion7(), role);

    private static string Serial() => $"SN-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    private async Task<DeviceResponse> AddInverterAsync(Guid installationId)
    {
        using var op = As(Roles.Operator);
        using var response = await op.PostJsonAsync($"/api/installations/{installationId}/devices", new AddDeviceRequest
        {
            Type = DeviceType.Inverter,
            SerialNumber = Serial(),
            Manufacturer = "Fronius",
            Model = "Symo 10",
            PowerW = 10000,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<DeviceResponse>();
    }

    private static VisitDeviceReplacementRequest Swap(Guid deviceId, string? serial = null) => new()
    {
        DeviceId = deviceId,
        Replacement = new ReplaceDeviceRequest
        {
            SerialNumber = serial ?? Serial(),
            Manufacturer = "Fronius",
            Model = "Symo 10",
            PowerW = 10000,
            Reason = "Porucha výkonovej časti",
        },
    };

    [Fact]
    public async Task Start_ByAssignedTechnician_MovesVisitInProgress_AndTicketOnSite()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var visit = await op.PlannedAsync(scenario);
        using var tech = As(Roles.Technician, scenario.TechnicianId);

        var started = await tech.PostAsync($"/api/visits/{visit.Id}/start", null).OkAsync<VisitResponse>();

        Assert.Equal(ServiceVisitState.InProgress, started.State);
        Assert.Equal(Clock, started.StartedAt);
        Assert.Equal(ServiceTicketState.OnSite, (await op.GetJsonAsync<TicketResponse>($"/api/tickets/{scenario.Ticket.Id}")).State);
    }

    [Fact]
    public async Task Start_ByAnotherTechnician_Returns404_AndTwiceReturns409()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var visit = await op.PlannedAsync(scenario);
        var stranger = await InstallationData.SeedTechnicianAsync(_factory);
        using var other = As(Roles.Technician, stranger);
        using var owner = As(Roles.Technician, scenario.TechnicianId);

        using var foreign = await other.PostAsync($"/api/visits/{visit.Id}/start", null);
        using var first = await owner.PostAsync($"/api/visits/{visit.Id}/start", null);
        using var again = await owner.PostAsync($"/api/visits/{visit.Id}/start", null);
        using var missing = await owner.PostAsync($"/api/visits/{Guid.NewGuid()}/start", null);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Report_WritesProtocol_CompletesVisit_AndShowsItInTheDetail()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var started = await op.StartedAsync(scenario);
        _clock.Advance(Day(12) - Clock);
        using var tech = As(Roles.Technician, scenario.TechnicianId);

        var done = await tech.PostJsonAsync($"/api/visits/{started.Id}/report", Report(Day(9), VisitCause.NoFaultFound)).OkAsync<VisitResponse>();

        Assert.Equal(ServiceVisitState.Done, done.State);
        Assert.NotNull(done.Report);
        Assert.Equal(VisitCause.NoFaultFound, done.Report.Cause);
        Assert.Equal("Invertor hlási chybu izolácie DC.", done.Report.Findings);
        Assert.Equal(Day(9), done.Report.ArrivedAt);
        Assert.Equal(Day(9).AddMinutes(75), done.Report.DepartedAt);
        Assert.Equal(scenario.TechnicianId, done.Report.ReportedBy);
        Assert.Equal(Clock + (Day(12) - Clock), done.Report.ReportedAt);
        var detail = await op.GetJsonAsync<VisitResponse>($"/api/visits/{started.Id}");
        Assert.Equal(VisitCause.NoFaultFound, detail.Report!.Cause);
    }

    [Fact]
    public async Task Report_CannotBeChangedAfterwards_AndNeedsAStartedVisit()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var planned = await op.PlannedAsync(scenario);
        using var early = await op.PostJsonAsync($"/api/visits/{planned.Id}/report", Report(Day(9)));
        await op.PostAsync($"/api/visits/{planned.Id}/start", null).OkAsync<VisitResponse>();
        _clock.Advance(Day(12) - Clock);
        await op.PostJsonAsync($"/api/visits/{planned.Id}/report", Report(Day(9))).OkAsync<VisitResponse>();

        using var second = await op.PostJsonAsync($"/api/visits/{planned.Id}/report", Report(Day(9), VisitCause.Other));

        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var detail = await op.GetJsonAsync<VisitResponse>($"/api/visits/{planned.Id}");
        Assert.Equal(VisitCause.InverterFault, detail.Report!.Cause);
    }

    [Fact]
    public async Task Report_InvalidData_Returns400()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var started = await op.StartedAsync(scenario);
        _clock.Advance(Day(12) - Clock);
        var url = $"/api/visits/{started.Id}/report";

        using var reversed = await op.PostJsonAsync(url, Report(Day(9)) with { DepartedAt = Day(9).AddMinutes(-5) });
        using var future = await op.PostJsonAsync(url, Report(Day(9)) with { DepartedAt = Day(13) });
        using var blank = await op.PostJsonAsync(url, Report(Day(9)) with { WorkDone = "  " });
        using var noCause = await op.PostJsonAsync(url, Report(Day(9)) with { Cause = null });

        Assert.All([reversed, future, blank, noCause], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }

    [Fact]
    public async Task Report_ByAnotherTechnician_Returns404()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var started = await op.StartedAsync(scenario);
        _clock.Advance(Day(12) - Clock);
        using var other = As(Roles.Technician, await InstallationData.SeedTechnicianAsync(_factory));

        using var response = await other.PostJsonAsync($"/api/visits/{started.Id}/report", Report(Day(9)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Materials_AreStoredWithTicketAndVisit_AndReplaceThePreviousList()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var started = await op.StartedAsync(scenario);
        using var tech = As(Roles.Technician, scenario.TechnicianId);

        var first = await tech.PutJsonAsync($"/api/visits/{started.Id}/materials", new ReplaceVisitMaterialsRequest
        {
            Materials =
            [
                new VisitMaterialRequest { Name = " DC konektor MC4 ", Quantity = 2, Unit = "ks" },
                new VisitMaterialRequest { Name = "Kábel 6 mm2", Quantity = 3.5m, Unit = "m" },
            ],
        }).OkAsync<VisitResponse>();
        var second = await tech.PutJsonAsync($"/api/visits/{started.Id}/materials", new ReplaceVisitMaterialsRequest
        {
            Materials = [new VisitMaterialRequest { Name = "Poistka 16A", Quantity = 1 }],
        }).OkAsync<VisitResponse>();

        Assert.Equal(2, first.Materials.Count);
        Assert.Contains(first.Materials, m => m is { Name: "DC konektor MC4", Quantity: 2, Unit: "ks" });
        Assert.Contains(first.Materials, m => m is { Name: "Kábel 6 mm2", Quantity: 3.5m, Unit: "m" });
        var only = Assert.Single(second.Materials);
        Assert.Equal("ks", only.Unit);
        await _factory.ExecuteDbAsync(async db =>
        {
            var rows = db.Set<ServiceVisitMaterial>().Where(m => m.ServiceVisitId == started.Id).ToList();
            Assert.Single(rows);
            Assert.Equal(scenario.Ticket.Id, rows[0].ServiceTicketId);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Materials_InvalidItems_Return400_AndNotOnSiteReturns409()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var planned = await op.PlannedAsync(scenario);
        var valid = new ReplaceVisitMaterialsRequest { Materials = [new VisitMaterialRequest { Name = "Poistka", Quantity = 1 }] };

        using var notStarted = await op.PutJsonAsync($"/api/visits/{planned.Id}/materials", valid);
        await op.PostAsync($"/api/visits/{planned.Id}/start", null).OkAsync<VisitResponse>();
        using var zero = await op.PutJsonAsync($"/api/visits/{planned.Id}/materials",
            new ReplaceVisitMaterialsRequest { Materials = [new VisitMaterialRequest { Name = "Poistka", Quantity = 0 }] });
        using var blank = await op.PutJsonAsync($"/api/visits/{planned.Id}/materials",
            new ReplaceVisitMaterialsRequest { Materials = [new VisitMaterialRequest { Name = " ", Quantity = 1 }] });
        using var missing = await op.PutJsonAsync($"/api/visits/{planned.Id}/materials", new ReplaceVisitMaterialsRequest());

        Assert.Equal(HttpStatusCode.Conflict, notStarted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Materials_AfterVerification_Return409()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var done = await op.ReportedAsync(scenario, _clock);
        await op.PostAsync($"/api/tickets/{scenario.Ticket.Id}/verify", null).OkAsync<TicketStateResponse>();

        using var response = await op.PutJsonAsync($"/api/visits/{done.Id}/materials",
            new ReplaceVisitMaterialsRequest { Materials = [new VisitMaterialRequest { Name = "Poistka", Quantity = 1 }] });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeviceReplacement_UsesTheInstallationFlow_KeepsHistory_AndLinksTheVisit()
    {
        var scenario = await ScenarioAsync(_factory);
        var inverter = await AddInverterAsync(scenario.Ticket.InstallationId);
        using var op = As(Roles.Operator);
        var started = await op.StartedAsync(scenario);
        using var tech = As(Roles.Technician, scenario.TechnicianId);
        var newSerial = Serial();

        using var response = await tech.PostJsonAsync($"/api/visits/{started.Id}/device-replacements", Swap(inverter.Id, newSerial));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.ReadJsonAsync<VisitDeviceReplacementResult>();
        Assert.Equal(inverter.Id, result.Link.OldDeviceId);
        Assert.Equal(result.Replacement.Id, result.Link.NewDeviceId);
        Assert.Equal(DeviceStatus.Replaced, result.Replaced.Status);
        Assert.Equal(newSerial, result.Replacement.SerialNumber);
        Assert.Equal(inverter.Id, result.Replacement.ReplacesDeviceId);

        var installed = await op.GetJsonAsync<List<DeviceResponse>>($"/api/installations/{scenario.Ticket.InstallationId}/devices");
        var all = await op.GetJsonAsync<List<DeviceResponse>>($"/api/installations/{scenario.Ticket.InstallationId}/devices?includeReplaced=true");
        Assert.Equal(newSerial, Assert.Single(installed).SerialNumber);
        Assert.Equal(2, all.Count);
        var detail = await op.GetJsonAsync<VisitResponse>($"/api/visits/{started.Id}");
        var link = Assert.Single(detail.DeviceReplacements);
        Assert.Equal(inverter.Id, link.OldDeviceId);
        Assert.Equal("Porucha výkonovej časti", link.Reason);
    }

    [Fact]
    public async Task DeviceReplacement_DuplicateSerial_Returns409_AndWritesNoLink()
    {
        var scenario = await ScenarioAsync(_factory);
        var inverter = await AddInverterAsync(scenario.Ticket.InstallationId);
        var taken = await AddInverterAsync(scenario.Ticket.InstallationId);
        using var op = As(Roles.Operator);
        var started = await op.StartedAsync(scenario);

        using var response = await op.PostJsonAsync($"/api/visits/{started.Id}/device-replacements", Swap(inverter.Id, taken.SerialNumber));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty((await op.GetJsonAsync<VisitResponse>($"/api/visits/{started.Id}")).DeviceReplacements);
        var devices = await op.GetJsonAsync<List<DeviceResponse>>($"/api/installations/{scenario.Ticket.InstallationId}/devices");
        Assert.Contains(devices, d => d.Id == inverter.Id && d.Status == DeviceStatus.Installed);
    }

    [Fact]
    public async Task DeviceReplacement_OfADeviceFromAnotherInstallation_Returns404_AndInvalidSerialReturns400()
    {
        var scenario = await ScenarioAsync(_factory);
        var foreign = await ScenarioAsync(_factory);
        var foreignInverter = await AddInverterAsync(foreign.Ticket.InstallationId);
        var inverter = await AddInverterAsync(scenario.Ticket.InstallationId);
        using var op = As(Roles.Operator);
        var started = await op.StartedAsync(scenario);

        using var otherInstallation = await op.PostJsonAsync($"/api/visits/{started.Id}/device-replacements", Swap(foreignInverter.Id));
        using var blankSerial = await op.PostJsonAsync($"/api/visits/{started.Id}/device-replacements", Swap(inverter.Id, " "));
        using var noBody = await op.PostJsonAsync($"/api/visits/{started.Id}/device-replacements", new VisitDeviceReplacementRequest());

        Assert.Equal(HttpStatusCode.NotFound, otherInstallation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blankSerial.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noBody.StatusCode);
    }

    [Fact]
    public async Task DeviceReplacement_BeforeArrival_Returns409()
    {
        var scenario = await ScenarioAsync(_factory);
        var inverter = await AddInverterAsync(scenario.Ticket.InstallationId);
        using var op = As(Roles.Operator);
        var planned = await op.PlannedAsync(scenario);

        using var response = await op.PostJsonAsync($"/api/visits/{planned.Id}/device-replacements", Swap(inverter.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Verify_RequiresProtocol_AndMovesTicketToVerified()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var started = await op.StartedAsync(scenario);
        using var tech = As(Roles.Technician, scenario.TechnicianId);

        using var withoutProtocol = await tech.PostAsync($"/api/tickets/{scenario.Ticket.Id}/verify", null);
        _clock.Advance(Day(12) - Clock);
        await tech.PostJsonAsync($"/api/visits/{started.Id}/report", Report(Day(9))).OkAsync<VisitResponse>();
        var verified = await tech.PostAsync($"/api/tickets/{scenario.Ticket.Id}/verify", null).OkAsync<TicketStateResponse>();

        Assert.Equal(HttpStatusCode.Conflict, withoutProtocol.StatusCode);
        Assert.Equal(ServiceTicketState.Verified, verified.State);
    }

    [Fact]
    public async Task Verify_ByAnotherTechnician_Returns404_AndUnknownTicketReturns404()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.ReportedAsync(scenario, _clock);
        using var other = As(Roles.Technician, await InstallationData.SeedTechnicianAsync(_factory));

        using var foreign = await other.PostAsync($"/api/tickets/{scenario.Ticket.Id}/verify", null);
        using var missing = await other.PostAsync($"/api/tickets/{Guid.NewGuid()}/verify", null);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Close_AfterVerification_WalksResolvedAndClosed_AndLocksTheProtocol()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var done = await op.ReportedAsync(scenario, _clock);
        await op.PostAsync($"/api/tickets/{scenario.Ticket.Id}/verify", null).OkAsync<TicketStateResponse>();

        var closed = await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/close", new CloseTicketRequest { Note = "výroba obnovená" }).OkAsync<TicketStateResponse>();

        Assert.Equal(ServiceTicketState.Closed, closed.State);
        var history = await op.GetJsonAsync<List<TicketStateChangeResponse>>($"/api/tickets/{scenario.Ticket.Id}/history");
        Assert.Equal(
            [ServiceTicketState.VisitScheduled, ServiceTicketState.OnSite, ServiceTicketState.Verified, ServiceTicketState.Resolved, ServiceTicketState.Closed],
            history.Skip(3).Select(h => h.ToState));
        using var report = await op.PostJsonAsync($"/api/visits/{done.Id}/report", Report(Day(9)));
        using var materials = await op.PutJsonAsync($"/api/visits/{done.Id}/materials",
            new ReplaceVisitMaterialsRequest { Materials = [new VisitMaterialRequest { Name = "x", Quantity = 1 }] });
        Assert.Equal(HttpStatusCode.Conflict, report.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, materials.StatusCode);
    }

    [Theory]
    [InlineData(ServiceTicketState.VisitScheduled)]
    [InlineData(ServiceTicketState.OnSite)]
    public async Task Close_BeforeTheProtocolIsVerified_Returns409(ServiceTicketState target)
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        if (target == ServiceTicketState.OnSite)
        {
            await op.StartedAsync(scenario);
        }
        else
        {
            await op.PlannedAsync(scenario);
        }

        using var response = await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/close", new CloseTicketRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(target, (await op.GetJsonAsync<TicketResponse>($"/api/tickets/{scenario.Ticket.Id}")).State);
    }

    [Fact]
    public async Task Close_RemoteResolution_NeedsNoVisit()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);

        var closed = await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/close", new CloseTicketRequest()).OkAsync<TicketStateResponse>();

        Assert.Equal(ServiceTicketState.Closed, closed.State);
    }

    [Fact]
    public async Task Confirm_ByOwner_RecordsConfirmation_OnlyOnce_AndOnlyWhenClosed()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        using var owner = As(Roles.Customer, scenario.Seed.UserA);

        using var open = await owner.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/confirm", new ConfirmTicketRequest());
        await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/close", new CloseTicketRequest()).OkAsync<TicketStateResponse>();
        _clock.Advance(TimeSpan.FromHours(3));
        var confirmed = await owner.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/confirm", new ConfirmTicketRequest { Comment = "Funguje, ďakujem" })
            .OkAsync<TicketConfirmationResponse>();
        using var again = await owner.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/confirm", new ConfirmTicketRequest());

        Assert.Equal(HttpStatusCode.Conflict, open.StatusCode);
        Assert.Equal(TicketConfirmationDecision.Confirmed, confirmed.Decision);
        Assert.Equal("Funguje, ďakujem", confirmed.Comment);
        Assert.Equal(Clock.AddHours(3), confirmed.DecidedAt);
        Assert.Equal(ServiceTicketState.Closed, confirmed.TicketState);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Confirm_AndReopen_OfForeignTicket_Return404()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/close", new CloseTicketRequest()).OkAsync<TicketStateResponse>();
        using var stranger = As(Roles.Customer, scenario.Seed.UserB);

        using var confirm = await stranger.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/confirm", new ConfirmTicketRequest());
        using var reopen = await stranger.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/reopen", new ReopenTicketRequest { Reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reopen.StatusCode);
        Assert.Equal(ServiceTicketState.Closed, (await op.GetJsonAsync<TicketResponse>($"/api/tickets/{scenario.Ticket.Id}")).State);
    }

    [Fact]
    public async Task Reopen_WithinSevenDays_ReturnsTicketToRemoteDiagnosis_AndAllowsANewVisit()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.ClosedAsync(scenario, _clock);
        using var owner = As(Roles.Customer, scenario.Seed.UserA);
        _clock.Advance(TimeSpan.FromDays(6));

        var reopened = await owner.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/reopen", new ReopenTicketRequest { Reason = "Porucha sa vrátila" })
            .OkAsync<TicketConfirmationResponse>();

        Assert.Equal(TicketConfirmationDecision.Reopened, reopened.Decision);
        Assert.Equal(ServiceTicketState.RemoteDiagnosis, reopened.TicketState);
        var history = await owner.GetJsonAsync<List<TicketStateChangeResponse>>($"/api/tickets/{scenario.Ticket.Id}/history");
        Assert.Equal(ServiceTicketState.Closed, history[^1].FromState);
        Assert.Equal(ServiceTicketState.RemoteDiagnosis, history[^1].ToState);
        Assert.Null(history[^1].Note);

        // The same technician can be scheduled again for a second visit.
        var second = await op.PlanAsync(scenario.Ticket.Id, _clock.GetUtcNow().AddDays(1));
        Assert.Equal(ServiceVisitState.Planned, second.State);
    }

    [Fact]
    public async Task Reopen_AfterSevenDays_Returns409_AndKeepsTheTicketClosed()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/close", new CloseTicketRequest()).OkAsync<TicketStateResponse>();
        using var owner = As(Roles.Customer, scenario.Seed.UserA);
        _clock.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1)));

        using var response = await owner.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/reopen", new ReopenTicketRequest { Reason = "Zase nevyrába" });
        var problem = await response.ReadJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("7 days", problem.Detail);
        Assert.Equal(ServiceTicketState.Closed, (await op.GetJsonAsync<TicketResponse>($"/api/tickets/{scenario.Ticket.Id}")).State);
    }

    [Fact]
    public async Task Reopen_NotClosedTicket_Returns409_AndMissingReasonReturns400()
    {
        var scenario = await ScenarioAsync(_factory);
        using var owner = As(Roles.Customer, scenario.Seed.UserA);

        using var open = await owner.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/reopen", new ReopenTicketRequest { Reason = "x" });
        using var noReason = await owner.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/reopen", new ReopenTicketRequest { Reason = " " });

        Assert.Equal(HttpStatusCode.Conflict, open.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
    }

    [Fact]
    public async Task GenericTransition_StillCannotReopenOrDriveVisitStates()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/close", new CloseTicketRequest()).OkAsync<TicketStateResponse>();

        using var reopen = await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/transition", new TransitionTicketRequest { State = ServiceTicketState.RemoteDiagnosis });

        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);
    }

    public static TheoryData<string, string, string, string[]> Endpoints => new()
    {
        { "PUT", "/api/tickets/{0}/technician", "TicketsManage", [Roles.Admin, Roles.Operator] },
        { "POST", "/api/tickets/{0}/visits", "TicketsManage", [Roles.Admin, Roles.Operator] },
        { "POST", "/api/tickets/{0}/close", "TicketsManage", [Roles.Admin, Roles.Operator] },
        { "PUT", "/api/visits/{0}/schedule", "TicketsManage", [Roles.Admin, Roles.Operator] },
        { "GET", "/api/technicians/{0}/visits", "TicketsManage", [Roles.Admin, Roles.Operator] },
        { "GET", "/api/technicians/me/visits", "TicketsField", [Roles.Admin, Roles.Operator, Roles.Technician] },
        { "POST", "/api/visits/{0}/start", "TicketsField", [Roles.Admin, Roles.Operator, Roles.Technician] },
        { "POST", "/api/visits/{0}/report", "TicketsField", [Roles.Admin, Roles.Operator, Roles.Technician] },
        { "PUT", "/api/visits/{0}/materials", "TicketsField", [Roles.Admin, Roles.Operator, Roles.Technician] },
        { "POST", "/api/visits/{0}/device-replacements", "TicketsField", [Roles.Admin, Roles.Operator, Roles.Technician] },
        { "POST", "/api/tickets/{0}/verify", "TicketsField", [Roles.Admin, Roles.Operator, Roles.Technician] },
        { "POST", "/api/tickets/{0}/confirm", "TicketsConfirm", [Roles.Customer] },
        { "POST", "/api/tickets/{0}/reopen", "TicketsConfirm", [Roles.Customer] },
        { "GET", "/api/tickets/{0}/visits", "TicketsRead", [Roles.Admin, Roles.Operator, Roles.Sales, Roles.Designer, Roles.Technician, Roles.Customer] },
        { "GET", "/api/visits/{0}", "TicketsRead", [Roles.Admin, Roles.Operator, Roles.Sales, Roles.Designer, Roles.Technician, Roles.Customer] },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_Returns401Anonymous_And403ForRolesOutsideThePolicy(string method, string template, string policy, string[] allowed)
    {
        var url = string.Format(template, Guid.CreateVersion7());
        HttpRequestMessage Request() => new(new HttpMethod(method), url)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { }),
        };

        using var anonymous = _factory.CreateApiClient();
        using var unauthorized = await anonymous.SendAsync(Request());
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        Assert.Equal(AuthorizationPolicies.Matrix[policy].Order(), allowed.Order());
        foreach (var role in Roles.All.Except(allowed))
        {
            using var client = As(role);
            using var forbidden = await client.SendAsync(Request());
            Assert.True(forbidden.StatusCode == HttpStatusCode.Forbidden, $"{role} on {method} {template}: {(int)forbidden.StatusCode}");
        }
    }
}
