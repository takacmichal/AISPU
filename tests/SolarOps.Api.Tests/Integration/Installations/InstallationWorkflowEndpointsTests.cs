using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;
using SolarOps.Api.Tests.Unit.Installations;

namespace SolarOps.Api.Tests.Integration.Installations;

/// <summary>Assembly checklist (#90), revision report (#91), commissioning (#92) and documentation hand-over (#93).</summary>
[Collection(PostgresCollection.Name)]
public sealed class InstallationWorkflowEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 3);

    private readonly ApiFactory _factory = new ClockedApiFactory(postgres, new FixedTimeProvider(Now));

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private static string Url(Guid id, string path) => $"/api/installations/{id}{path}";

    private HttpClient Operator() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    private HttpClient Technician(Guid? id = null) => _factory.CreateApiClient(id ?? Guid.CreateVersion7(), Roles.Technician);

    private async Task<InstallationResponse> InStateAsync(InstallationState state, Guid? customerId = null, bool approved = true)
    {
        using var client = Operator();
        var request = InstallationsTestData.NewInstallationRequest();
        var installation = (await client.CreateInstallationAsync(customerId is { } c ? request with { CustomerId = c } : request)).Installation;
        if (approved)
        {
            await _factory.SeedConnectionRequestAsync(installation);
        }

        await _factory.WalkToAsync(installation.Id, state, Now);
        return installation;
    }

    private static RecordRevisionReportRequest PassedReport() => new()
    {
        InspectedOn = Today,
        InspectorName = "Ing. Ján Revízny",
        InspectorCertificateNumber = "REV-123/2026",
        Result = RevisionResult.Passed,
        ValidUntil = Today.AddYears(4),
        FileName = "revizna-sprava.pdf",
        ContentType = "application/pdf",
        ContentBase64 = Convert.ToBase64String(InstallationLifecycle.PdfBytes),
    };

    private static ChecklistStep FirstRequired => ChecklistTemplate.Steps.First(s => s.Required);

    // ---------- #90 checklist ----------

    [Fact]
    public async Task Checklist_Get_ReturnsTemplateWithStepStates()
    {
        var installation = await InStateAsync(InstallationState.Mounting);
        using var client = Technician();

        using var response = await client.GetAsync(Url(installation.Id, "/checklist"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var checklist = (await response.ReadAsync<ChecklistResponse>())!;
        Assert.True(checklist.Editable);
        Assert.False(checklist.Complete);
        Assert.Equal(ChecklistTemplate.Steps.Select(s => s.Code), checklist.Steps.Select(s => s.Code));
        Assert.True(checklist.Steps.Count >= 8);
        Assert.All(checklist.Steps, s => Assert.False(s.Done));
        Assert.Equal(ChecklistTemplate.Steps.Count(s => s.Required), checklist.MissingRequiredSteps.Count);
    }

    [Fact]
    public async Task Checklist_Put_MarksStep_WithTimestampAndAuthor()
    {
        var installation = await InStateAsync(InstallationState.Mounting);
        var technicianId = Guid.CreateVersion7();
        using var client = Technician(technicianId);

        using var response = await client.PutJsonAsync(
            Url(installation.Id, $"/checklist/{FirstRequired.Code}"), new UpdateChecklistStepRequest { Done = true, Note = "Lešenie OK" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var step = (await response.ReadAsync<ChecklistResponse>())!.Steps.Single(s => s.Code == FirstRequired.Code);
        Assert.True(step.Done);
        Assert.Equal("Lešenie OK", step.Note);
        Assert.Equal(technicianId, step.ChangedBy);
        Assert.Equal(Now, step.ChangedAt);
    }

    [Fact]
    public async Task Checklist_Put_Validation_UnknownStep_WrongState()
    {
        var mounting = await InStateAsync(InstallationState.Mounting);
        var planned = await InStateAsync(InstallationState.Planned);
        using var client = Technician();

        using var missingDone = await client.PutJsonAsync(Url(mounting.Id, $"/checklist/{FirstRequired.Code}"), new { note = "x" });
        using var unknownStep = await client.PutJsonAsync(Url(mounting.Id, "/checklist/NoSuchStep"), new UpdateChecklistStepRequest { Done = true });
        using var unknownInstallation = await client.PutJsonAsync(
            Url(Guid.CreateVersion7(), $"/checklist/{FirstRequired.Code}"), new UpdateChecklistStepRequest { Done = true });
        using var wrongState = await client.PutJsonAsync(Url(planned.Id, $"/checklist/{FirstRequired.Code}"), new UpdateChecklistStepRequest { Done = true });

        Assert.Equal(HttpStatusCode.BadRequest, missingDone.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownStep.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownInstallation.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, wrongState.StatusCode);
    }

    [Fact]
    public async Task CompleteMounting_RequiresAllRequiredSteps()
    {
        var installation = await InStateAsync(InstallationState.Mounting);
        using var client = Technician();
        var transition = new InstallationTransitionRequest { TargetState = InstallationState.MountingDone };

        using var blocked = await client.PostJsonAsync(Url(installation.Id, "/transitions"), transition);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains(FirstRequired.Code, await blocked.Content.ReadAsStringAsync());

        foreach (var step in ChecklistTemplate.Steps.Where(s => s.Required))
        {
            (await client.PutJsonAsync(Url(installation.Id, $"/checklist/{step.Code}"), new UpdateChecklistStepRequest { Done = true }))
                .EnsureSuccessStatusCode();
        }

        using var done = await client.PostJsonAsync(Url(installation.Id, "/transitions"), transition);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var checklist = await (await client.GetAsync(Url(installation.Id, "/checklist"))).ReadAsync<ChecklistResponse>();
        Assert.True(checklist!.Complete);
        Assert.False(checklist.Editable);
    }

    // ---------- #91 revision report ----------

    [Fact]
    public async Task RevisionReport_Passed_IsStored_AndMovesToInspectionPassed()
    {
        var installation = await InStateAsync(InstallationState.Inspection);
        using var client = Technician();

        using var response = await client.PostJsonAsync(Url(installation.Id, "/revision-reports"), PassedReport());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var report = (await response.ReadAsync<RevisionReportResponse>())!;
        Assert.Equal(Url(installation.Id, $"/revision-reports/{report.Id}"), response.Headers.Location?.OriginalString);
        Assert.Equal(RevisionResult.Passed, report.Result);
        Assert.Equal(Today.AddYears(4), report.ValidUntil);
        Assert.Equal("Ing. Ján Revízny", report.InspectorName);
        Assert.Equal(InstallationLifecycle.PdfBytes.Length, report.SizeBytes);
        Assert.Equal(64, report.Sha256.Length);

        var detail = await (await client.GetAsync(Url(installation.Id, ""))).ReadAsync<InstallationDetailResponse>();
        Assert.Equal(InstallationState.InspectionPassed, detail!.Installation.State);
        Assert.Equal(RevisionResult.Passed, detail.Installation.LastRevisionResult);

        var list = await (await client.GetAsync(Url(installation.Id, "/revision-reports"))).ReadAsync<List<RevisionReportResponse>>();
        Assert.Equal([report.Id], list!.Select(r => r.Id));
        Assert.DoesNotContain("contentBase64", await (await client.GetAsync(Url(installation.Id, "/revision-reports"))).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RevisionReport_Failed_ReturnsInstallationToRepairs()
    {
        var installation = await InStateAsync(InstallationState.Inspection);
        using var client = Technician();
        var failed = PassedReport() with { Result = RevisionResult.Failed, ValidUntil = null, Findings = "Chýba uzemnenie konštrukcie" };

        using var response = await client.PostJsonAsync(Url(installation.Id, "/revision-reports"), failed);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var detail = await (await client.GetAsync(Url(installation.Id, ""))).ReadAsync<InstallationDetailResponse>();
        Assert.Equal(InstallationState.InspectionFailed, detail!.Installation.State);
        Assert.Equal("Chýba uzemnenie konštrukcie", detail.StateHistory[^1].Note);

        using var rework = await client.PostJsonAsync(Url(installation.Id, "/transitions"), new InstallationTransitionRequest { TargetState = InstallationState.Mounting });
        Assert.Equal(HttpStatusCode.OK, rework.StatusCode);
    }

    [Fact]
    public async Task RevisionReport_InvalidInput_Returns400()
    {
        var installation = await InStateAsync(InstallationState.Inspection);
        using var client = Technician();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var invalid = new (string Field, RecordRevisionReportRequest Request)[]
        {
            ("ValidUntil", PassedReport() with { ValidUntil = null }),
            ("ValidUntil", PassedReport() with { ValidUntil = Today }),
            ("Findings", PassedReport() with { Result = RevisionResult.Failed, ValidUntil = null }),
            ("ContentType", PassedReport() with { ContentType = "text/html" }),
            ("ContentBase64", PassedReport() with { ContentBase64 = "not base64!" }),
            ("ContentBase64", PassedReport() with { ContentBase64 = Convert.ToBase64String(png) }),
            ("ContentBase64", PassedReport() with { ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<script>alert(1)</script>")) }),
            ("InspectedOn", PassedReport() with { InspectedOn = Today.AddDays(1) }),
            ("InspectorName", PassedReport() with { InspectorName = " " }),
        };

        foreach (var (field, request) in invalid)
        {
            using var response = await client.PostJsonAsync(Url(installation.Id, "/revision-reports"), request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(field, await response.Content.ReadAsStringAsync());
        }

        var detail = await (await client.GetAsync(Url(installation.Id, ""))).ReadAsync<InstallationDetailResponse>();
        Assert.Equal(InstallationState.Inspection, detail!.Installation.State);
    }

    [Fact]
    public async Task RevisionReport_OutsideInspection_Returns409_UnknownInstallation404()
    {
        var installation = await InStateAsync(InstallationState.MountingDone);
        using var client = Technician();

        using var wrongState = await client.PostJsonAsync(Url(installation.Id, "/revision-reports"), PassedReport());
        using var unknown = await client.PostJsonAsync(Url(Guid.CreateVersion7(), "/revision-reports"), PassedReport());

        Assert.Equal(HttpStatusCode.Conflict, wrongState.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task RevisionReport_File_IsDownloadedSafely_AndTheDownloadIsRecorded()
    {
        var installation = await InStateAsync(InstallationState.Inspection);
        var userId = Guid.CreateVersion7();
        using var client = _factory.CreateApiClient(userId, Roles.Operator);
        var report = (await (await client.PostJsonAsync(Url(installation.Id, "/revision-reports"), PassedReport())).ReadAsync<RevisionReportResponse>())!;

        using var response = await client.GetAsync(Url(installation.Id, $"/revision-reports/{report.Id}/file"));
        using var unknown = await client.GetAsync(Url(installation.Id, $"/revision-reports/{Guid.CreateVersion7()}/file"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(InstallationLifecycle.PdfBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("revizna-sprava.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var downloads = await _factory.ExecuteDbAsync(db => db.Set<InstallationDocumentDownload>().AsNoTracking().ToListAsync());
        var download = Assert.Single(downloads);
        Assert.Equal(InstallationDocumentKind.RevisionReport, download.Kind);
        Assert.Equal(report.Id, download.DocumentId);
        Assert.Equal(userId, download.DownloadedBy);
        Assert.Equal(Now, download.DownloadedAt);
    }

    [Fact]
    public async Task Transition_CannotSkipRevisionOrCommissioning()
    {
        var inspection = await InStateAsync(InstallationState.Inspection);
        var passed = await InStateAsync(InstallationState.InspectionPassed);
        using var client = Operator();

        using var toPassed = await client.PostJsonAsync(Url(inspection.Id, "/transitions"), new InstallationTransitionRequest { TargetState = InstallationState.InspectionPassed });
        using var toCommissioned = await client.PostJsonAsync(Url(passed.Id, "/transitions"), new InstallationTransitionRequest { TargetState = InstallationState.Commissioned });

        Assert.Equal(HttpStatusCode.Conflict, toPassed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, toCommissioned.StatusCode);
    }

    // ---------- #92 commissioning ----------

    private static CommissionInstallationRequest Protocol() => new() { CommissionedOn = Today, AcceptedBy = "Peter Zákazník", Note = "Výroba overená" };

    [Fact]
    public async Task Commission_SetsInOperation_AndShowsInMonitoring()
    {
        var installation = await InStateAsync(InstallationState.InspectionPassed);
        var technicianId = Guid.CreateVersion7();
        using var client = Technician(technicianId);

        using var response = await client.PostJsonAsync(Url(installation.Id, "/commission"), Protocol());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.ReadAsync<InstallationDetailResponse>())!.Installation;
        Assert.Equal(InstallationState.Commissioned, body.State);
        Assert.True(body.MonitoringEnabled);
        Assert.Equal(Now, body.MonitoringEnabledAt);
        Assert.Equal(Today, body.CommissionedOn);

        var monitored = await (await client.GetAsync("/api/installations?monitoringEnabled=true")).ReadAsync<List<InstallationResponse>>();
        Assert.Equal([installation.Id], monitored!.Select(i => i.Id));
        var notMonitored = await (await client.GetAsync("/api/installations?monitoringEnabled=false")).ReadAsync<List<InstallationResponse>>();
        Assert.DoesNotContain(notMonitored!, i => i.Id == installation.Id);
    }

    [Fact]
    public async Task Commission_WithoutApprovedConnectionRequest_Returns409()
    {
        var installation = await InStateAsync(InstallationState.InspectionPassed, approved: false);
        using var client = Technician();

        using var response = await client.PostJsonAsync(Url(installation.Id, "/commission"), Protocol());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("connection request", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Commission_Validation_State_AndUnknown()
    {
        var installation = await InStateAsync(InstallationState.InspectionPassed);
        var notYet = await InStateAsync(InstallationState.MountingDone);
        using var client = Technician();

        using var missing = await client.PostJsonAsync(Url(installation.Id, "/commission"), new { commissionedOn = Today });
        using var future = await client.PostJsonAsync(Url(installation.Id, "/commission"), Protocol() with { CommissionedOn = Today.AddDays(1) });
        using var wrongState = await client.PostJsonAsync(Url(notYet.Id, "/commission"), Protocol());
        using var unknown = await client.PostJsonAsync(Url(Guid.CreateVersion7(), "/commission"), Protocol());

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("AcceptedBy", await missing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, wrongState.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    // ---------- #93 documentation ----------

    [Fact]
    public async Task Documentation_BeforeCommissioning_Returns409()
    {
        var installation = await InStateAsync(InstallationState.InspectionPassed);
        using var client = Operator();

        using var package = await client.GetAsync(Url(installation.Id, "/documentation"));
        using var pdf = await client.GetAsync(Url(installation.Id, "/documentation/handover-protocol"));

        Assert.Equal(HttpStatusCode.Conflict, package.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, pdf.StatusCode);
    }

    [Fact]
    public async Task Documentation_ContainsRevisionProtocolAndDevices_AndRecordsDownloads()
    {
        var installation = await InStateAsync(InstallationState.Planned);
        using var technician = Technician();
        (await technician.PostJsonAsync($"/api/installations/{installation.Id}/devices",
            new AddDeviceRequest { Type = DeviceType.Inverter, SerialNumber = "FRO-10-778899", Manufacturer = "Fronius", Model = "Symo 10", PowerW = 10000 }))
            .EnsureSuccessStatusCode();
        await _factory.WalkToAsync(installation.Id, InstallationState.HandedOver, Now);
        var userId = Guid.CreateVersion7();
        using var client = _factory.CreateApiClient(userId, Roles.Operator);

        using var pdf = await client.GetAsync(Url(installation.Id, "/documentation/handover-protocol"));
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));

        using var response = await client.GetAsync(Url(installation.Id, "/documentation"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = (await response.ReadAsync<InstallationDocumentationResponse>())!;
        Assert.Equal(InstallationState.HandedOver, doc.State);
        Assert.Equal(RevisionResult.Passed, doc.RevisionReport.Result);
        Assert.Equal("Peter Zákazník", doc.Protocol.AcceptedBy);
        Assert.Equal(Today, doc.Protocol.CommissionedOn);
        Assert.True(doc.Protocol.MonitoringEnabled);
        var device = Assert.Single(doc.Devices);
        Assert.Equal("FRO-10-778899", device.SerialNumber);
        var download = Assert.Single(doc.Downloads);
        Assert.Equal(InstallationDocumentKind.HandoverProtocol, download.Kind);
        Assert.Equal(userId, download.DownloadedBy);
        Assert.Equal(Now, download.DownloadedAt);
    }

    // ---------- customer scope, 401, 403 ----------

    private async Task<(Guid UserA, InstallationResponse Own, InstallationResponse Foreign, Guid ForeignReportId)> CustomerSeedAsync()
    {
        async Task<Guid> NewUserAsync()
        {
            var user = new AppUser { UserName = $"u-{Guid.NewGuid():N}@example.sk", Email = $"u-{Guid.NewGuid():N}@example.sk" };
            await _factory.ExecuteDbAsync(async db =>
            {
                db.Users.Add(user);
                await db.SaveChangesAsync();
            });
            return user.Id;
        }

        var userA = await NewUserAsync();
        var userB = await NewUserAsync();
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var customerA = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("a@example.sk") with { UserId = userA });
        var customerB = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("b@example.sk", lastName: "Kováč") with { UserId = userB });
        var own = await InStateAsync(InstallationState.HandedOver, customerA.Id);
        var foreign = await InStateAsync(InstallationState.HandedOver, customerB.Id);
        var reportId = await _factory.ExecuteDbAsync(db =>
            db.Set<RevisionReport>().Where(r => r.InstallationId == foreign.Id).Select(r => r.Id).SingleAsync());
        return (userA, own, foreign, reportId);
    }

    [Fact]
    public async Task Customer_SeesAndDownloadsOwnDocumentation()
    {
        var s = await CustomerSeedAsync();
        using var customer = _factory.CreateApiClient(s.UserA, Roles.Customer);

        var doc = await (await customer.GetAsync(Url(s.Own.Id, "/documentation"))).ReadAsync<InstallationDocumentationResponse>();
        using var report = await customer.GetAsync(Url(s.Own.Id, $"/revision-reports/{doc!.RevisionReport.Id}/file"));
        using var pdf = await customer.GetAsync(Url(s.Own.Id, "/documentation/handover-protocol"));
        using var checklist = await customer.GetAsync(Url(s.Own.Id, "/checklist"));

        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal(HttpStatusCode.OK, checklist.StatusCode);
        var after = await (await customer.GetAsync(Url(s.Own.Id, "/documentation"))).ReadAsync<InstallationDocumentationResponse>();
        Assert.Equal(2, after!.Downloads.Count);
        Assert.All(after.Downloads, d => Assert.Equal(s.UserA, d.DownloadedBy));
    }

    [Fact]
    public async Task Customer_ForeignInstallation_Returns404OnEveryReadEndpoint()
    {
        var s = await CustomerSeedAsync();
        using var customer = _factory.CreateApiClient(s.UserA, Roles.Customer);

        foreach (var path in new[]
                 {
                     "/checklist", "/revision-reports", $"/revision-reports/{s.ForeignReportId}", $"/revision-reports/{s.ForeignReportId}/file",
                     "/documentation", "/documentation/handover-protocol",
                 })
        {
            using var response = await customer.GetAsync(Url(s.Foreign.Id, path));
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{path} returned {(int)response.StatusCode}");
        }

        Assert.Equal(0, await _factory.ExecuteDbAsync(db => db.Set<InstallationDocumentDownload>().CountAsync()));
    }

    [Fact]
    public async Task Endpoints_Return401Anonymous_And403ForRolesOutsideThePolicy()
    {
        var installation = await InStateAsync(InstallationState.Mounting);
        var id = installation.Id;
        using var anonymous = _factory.CreateApiClient();
        using var noRole = _factory.CreateApiClient(Guid.CreateVersion7());
        using var customer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var reportId = Guid.CreateVersion7();

        string[] reads =
        [
            "/checklist", "/revision-reports", $"/revision-reports/{reportId}", $"/revision-reports/{reportId}/file",
            "/documentation", "/documentation/handover-protocol",
        ];
        foreach (var path in reads)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id, path))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await noRole.GetAsync(Url(id, path))).StatusCode);
        }

        var writes = new (string Path, Func<HttpClient, string, Task<HttpResponseMessage>> Send)[]
        {
            ($"/checklist/{FirstRequired.Code}", (c, u) => c.PutJsonAsync(u, new UpdateChecklistStepRequest { Done = true })),
            ("/revision-reports", (c, u) => c.PostJsonAsync(u, PassedReport())),
            ("/commission", (c, u) => c.PostJsonAsync(u, Protocol())),
        };
        foreach (var (path, send) in writes)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await send(anonymous, Url(id, path))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await send(customer, Url(id, path))).StatusCode);
            // Sales plans installations but does not do field work.
            Assert.Equal(HttpStatusCode.Forbidden, (await send(sales, Url(id, path))).StatusCode);
        }
    }
}
