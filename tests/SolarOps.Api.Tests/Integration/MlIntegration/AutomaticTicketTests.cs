using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.MlIntegration;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Features.Weather;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;
using SolarOps.Api.Tests.Infrastructure;
using InstallationsTestData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.MlIntegration;

/// <summary>
/// Tickets created from alarms (#33: #115, #116, #117) and the scheduled analysis, against a fake ML server. The clock is
/// fixed to 2026-06-21 12:30 UTC.
/// </summary>
public sealed class AutomaticTicketTests : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CurrentHour = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeMlServer _ml = new();
    private readonly TicketApiFactory _factory;

    public AutomaticTicketTests(PostgresFixture postgres)
    {
        _factory = new TicketApiFactory(postgres, _ml);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Operator() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    [Fact]
    public async Task OngoingAlarm_CreatesAnAutomaticTicketWithTheDeviationAndEvidence()
    {
        var installationId = await SeedInstallationAsync();

        var result = await AnalyzeAsync(installationId);

        Assert.Equal(1, result.TicketsCreated);
        Assert.Equal(0, result.TicketsSupplemented);
        var ticket = Assert.Single(await TicketsAsync(installationId));
        Assert.Equal(ServiceTicketOrigin.AnomalyDetected, ticket.Origin);
        Assert.Equal(ServiceTicketState.New, ticket.State);
        Assert.Equal(ServiceTicketPriority.Medium, ticket.Priority);
        Assert.StartsWith("Podvýroba: ", ticket.Title, StringComparison.Ordinal);
        Assert.Contains("60 %", ticket.Description, StringComparison.Ordinal);
        Assert.Contains("kW z očakávaných", ticket.Description, StringComparison.Ordinal);
        Assert.Equal(Now, ticket.CreatedAt);

        var alarm = Assert.Single(await AlarmsAsync(installationId));
        Assert.Equal(ticket.Id, alarm.TicketId);
    }

    [Fact]
    public async Task EndedAlarm_DoesNotCreateATicket()
    {
        var installationId = await SeedInstallationAsync();
        _ml.AlarmIsOngoing = false;

        var result = await AnalyzeAsync(installationId);

        Assert.Equal(0, result.TicketsCreated);
        Assert.Empty(await TicketsAsync(installationId));
        Assert.Null(Assert.Single(await AlarmsAsync(installationId)).TicketId);
    }

    [Fact]
    public async Task RepeatedRun_KeepsOneTicketAndAddsNoComments()
    {
        var installationId = await SeedInstallationAsync();

        await AnalyzeAsync(installationId);
        var second = await AnalyzeAsync(installationId);

        Assert.Equal(0, second.TicketsCreated);
        Assert.Equal(0, second.TicketsSupplemented);
        var ticket = Assert.Single(await TicketsAsync(installationId));
        Assert.Empty(await CommentsAsync(ticket.Id));
    }

    [Fact]
    public async Task OpenTicket_IsSupplementedWithAnInternalComment_InsteadOfADuplicate()
    {
        var installationId = await SeedInstallationAsync();
        var existing = await SeedTicketAsync(installationId, ServiceTicketOrigin.Manual);

        var result = await AnalyzeAsync(installationId);

        Assert.Equal(0, result.TicketsCreated);
        Assert.Equal(1, result.TicketsSupplemented);
        Assert.Single(await TicketsAsync(installationId));
        var comment = Assert.Single(await CommentsAsync(existing));
        Assert.False(comment.IsPublic);
        Assert.Contains("60 %", comment.Body, StringComparison.Ordinal);
        Assert.Equal(existing, Assert.Single(await AlarmsAsync(installationId)).TicketId);
    }

    [Fact]
    public async Task ClosedTicket_DoesNotAbsorbANewAlarm()
    {
        var installationId = await SeedInstallationAsync();
        var closed = await SeedTicketAsync(installationId, ServiceTicketOrigin.AnomalyDetected, close: true);

        var result = await AnalyzeAsync(installationId);

        Assert.Equal(1, result.TicketsCreated);
        var alarm = Assert.Single(await AlarmsAsync(installationId));
        Assert.NotEqual(closed, alarm.TicketId);
    }

    [Fact]
    public async Task HighLoss_RaisesThePriorityOfANewTicket_AsConfigured()
    {
        var installationId = await SeedInstallationAsync();
        _ml.EstimatedLostKwh = 75;

        await AnalyzeAsync(installationId);

        Assert.Equal(ServiceTicketPriority.High, Assert.Single(await TicketsAsync(installationId)).Priority);
        Assert.Equal(ServiceTicketPriority.High, Assert.Single(await AlarmsAsync(installationId)).TicketPriority);
    }

    [Fact]
    public async Task GrowingLoss_RaisesThePriorityOfTheLinkedTicket_AndSaysSoInternally()
    {
        var installationId = await SeedInstallationAsync();
        await AnalyzeAsync(installationId);

        _ml.EstimatedLostKwh = 75;
        await AnalyzeAsync(installationId);

        var ticket = Assert.Single(await TicketsAsync(installationId));
        Assert.Equal(ServiceTicketPriority.High, ticket.Priority);
        Assert.False(Assert.Single(await CommentsAsync(ticket.Id)).IsPublic);
    }

    [Fact]
    public async Task UnchangedAlarm_DoesNotOverwriteAPriorityLoweredByTheOperator()
    {
        var installationId = await SeedInstallationAsync();
        _ml.EstimatedLostKwh = 75;
        await AnalyzeAsync(installationId);
        var ticketId = Assert.Single(await TicketsAsync(installationId)).Id;
        await _factory.ExecuteDbAsync(async db =>
        {
            var ticket = await db.Set<ServiceTicket>().SingleAsync(t => t.Id == ticketId);
            ticket.Triage(ServiceTicketPriority.Low, Guid.CreateVersion7(), null, Now);
            await db.SaveChangesAsync();
            return true;
        });

        await AnalyzeAsync(installationId);

        Assert.Equal(ServiceTicketPriority.Low, Assert.Single(await TicketsAsync(installationId)).Priority);
    }

    [Fact]
    public async Task MlServiceDown_CreatesNoTicket()
    {
        var installationId = await SeedInstallationAsync();
        _ml.FailWith = HttpStatusCode.ServiceUnavailable;
        using var client = Operator();

        using var response = await client.PostAsync($"/api/installations/{installationId}/analysis", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(await TicketsAsync(installationId));
    }

    [Fact]
    public async Task ScheduledRun_AnalysesOnlyMonitoredInstallations_AndSkipsThoseThatCannotBeAnalysed()
    {
        var monitored = await SeedInstallationAsync(monitoring: true);
        var withoutDesign = await SeedInstallationAsync(monitoring: true, design: false);
        var notMonitored = await SeedInstallationAsync(monitoring: false);

        var result = await ScheduledRunAsync();

        Assert.Equal(new MlScheduledAnalysisResult(2, 1, 1, 0), result);
        Assert.Single(await TicketsAsync(monitored));
        Assert.Empty(await TicketsAsync(withoutDesign));
        Assert.Empty(await TicketsAsync(notMonitored));
    }

    [Fact]
    public async Task ScheduledRun_StopsAfterTheServiceIsUnavailable_InsteadOfWaitingForEveryInstallation()
    {
        await SeedInstallationAsync(monitoring: true);
        await SeedInstallationAsync(monitoring: true);
        _ml.FailWith = HttpStatusCode.ServiceUnavailable;

        var result = await ScheduledRunAsync();

        Assert.Equal(new MlScheduledAnalysisResult(2, 0, 0, 2), result);
        // One installation, two attempts of the first call.
        Assert.Equal(2, _ml.Calls);
    }

    [Fact]
    public async Task ScheduledRun_OneInstallationRejected_DoesNotStopTheOthers()
    {
        await SeedInstallationAsync(monitoring: true);
        await SeedInstallationAsync(monitoring: true);
        _ml.FailWith = HttpStatusCode.UnprocessableEntity;

        var result = await ScheduledRunAsync();

        Assert.Equal(new MlScheduledAnalysisResult(2, 0, 0, 2), result);
        Assert.Equal(2, _ml.Calls);
    }

    private async Task<MlAnalysisResponse> AnalyzeAsync(Guid installationId)
    {
        using var client = Operator();
        using var response = await client.PostAsync($"/api/installations/{installationId}/analysis", null);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<MlAnalysisResponse>();
    }

    private async Task<MlScheduledAnalysisResult> ScheduledRunAsync() =>
        await _factory.Services.GetRequiredService<IMlScheduledAnalysis>().RunAsync(CancellationToken.None);

    private Task<List<ServiceTicket>> TicketsAsync(Guid installationId) =>
        _factory.ExecuteDbAsync(db => db.Set<ServiceTicket>().AsNoTracking().Where(t => t.InstallationId == installationId).ToListAsync());

    private Task<List<ServiceTicketComment>> CommentsAsync(Guid ticketId) =>
        _factory.ExecuteDbAsync(db => db.Set<ServiceTicketComment>().AsNoTracking().Where(c => c.ServiceTicketId == ticketId).ToListAsync());

    private Task<List<UnderproductionAlarm>> AlarmsAsync(Guid installationId) =>
        _factory.ExecuteDbAsync(db => db.Set<UnderproductionAlarm>().AsNoTracking().Where(a => a.InstallationId == installationId).ToListAsync());

    private Task<Guid> SeedTicketAsync(Guid installationId, ServiceTicketOrigin origin, bool close = false) =>
        _factory.ExecuteDbAsync(async db =>
        {
            var by = Guid.CreateVersion7();
            var installation = await db.Set<Installation>().AsNoTracking().SingleAsync(i => i.Id == installationId);
            var ticket = ServiceTicket.Create(installationId, installation.CustomerId, "Porucha", "Popis", ServiceTicketPriority.Low, origin, [], by, Now);
            db.Add(ticket);
            if (close)
            {
                ticket.Triage(ServiceTicketPriority.Low, by, null, Now);
                ticket.TransitionTo(ServiceTicketState.RemoteDiagnosis, by, null, Now);
                ticket.Close(by, null, Now);
            }

            await db.SaveChangesAsync();
            return ticket.Id;
        });

    private async Task<Guid> SeedInstallationAsync(bool monitoring = false, bool design = true)
    {
        using var op = Operator();
        var created = (await InstallationsTestData.CreateInstallationAsync(op, InstallationsTestData.NewInstallationRequest(plannedPowerKwp: 5m))).Installation;
        await _factory.ExecuteDbAsync(async db =>
        {
            if (monitoring)
            {
                // Monitoring is switched on by the commissioning workflow; the workflow itself has its own tests.
                await db.Database.ExecuteSqlAsync($"UPDATE installations SET monitoring_enabled = true WHERE id = {created.Id}");
            }

            if (!design)
            {
                return true;
            }

            var installation = await db.Set<Installation>().AsNoTracking().SingleAsync(i => i.Id == created.Id);
            var userId = Guid.CreateVersion7();
            var fve = FveDesign.Request(installation.CustomerId, installation.SupplyPointId, null, userId, Now);
            fve.RecordSiteSurvey("Strecha v poriadku", userId, Now);
            fve.UpdateParameters(
                48.15,
                17.11,
                4500,
                ConsumptionProfile.Household,
                [new RoofPlane { Name = "Juh", AzimuthDeg = 0, TiltDeg = 35, UsableAreaM2 = 30, ShadingLossPercent = 0, RoofType = RoofType.PitchedTile }],
                Now);
            db.Add(fve);
            for (var i = 0; i < 3; i++)
            {
                db.Add(WeatherForecast.Create(
                    created.Id,
                    new WeatherForecastPoint(CurrentHour.UtcDateTime.AddHours(i - 2), 500, 400, 100, 20, 25, 2),
                    48.15,
                    17.11,
                    Now.UtcDateTime));
            }

            await db.SaveChangesAsync();
            return true;
        });
        return created.Id;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TicketApiFactory(PostgresFixture postgres, FakeMlServer ml) : ApiFactory(postgres)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Ml:BaseUrl", "http://ml.test");
            builder.UseSetting("Ml:RetryDelaySeconds", "0");
            builder.UseSetting("Ml:AnalysisEnabled", "false");
            builder.UseSetting("Ml:TicketMediumLostKwh", "10");
            builder.UseSetting("Ml:TicketHighLostKwh", "50");
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedClock(Now)));
                services.AddHttpClient<IMlServiceClient, MlServiceClient>().ConfigurePrimaryHttpMessageHandler(() => ml);
            });
        }
    }

    /// <summary>Answers like the ML service: one prediction per weather hour and one alarm per installation.</summary>
    private sealed class FakeMlServer : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        public HttpStatusCode? FailWith { get; set; }

        public bool AlarmIsOngoing { get; set; } = true;

        public double EstimatedLostKwh { get; set; } = 1.2;

        // The stub outlives each request, so the base class must not dispose it with a test client.
        protected override void Dispose(bool disposing)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (FailWith is { } status)
            {
                return new HttpResponseMessage(status);
            }

            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var json = request.RequestUri!.AbsolutePath == "/v1/predictions/production"
                ? Predictions(document.RootElement)
                : Detections(document.RootElement);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static string Predictions(JsonElement request)
        {
            var hours = request.GetProperty("weather").EnumerateArray().Select(hour =>
                new { timestamp = hour.GetProperty("timestamp").GetDateTimeOffset(), expectedPowerW = 1000.0, expectedEnergyWh = 1000.0 });
            return JsonSerializer.Serialize(new { modelVersion = "test-model-1", generatedAt = Now, predictions = hours });
        }

        private string Detections(JsonElement request)
        {
            var installationId = request.GetProperty("installationId").GetGuid();
            var start = request.GetProperty("points")[0].GetProperty("timestamp").GetDateTimeOffset();
            var bytes = installationId.ToByteArray();
            bytes[0] ^= 0x5A;
            var alarm = new
            {
                alarmId = new Guid(bytes),
                start,
                end = AlarmIsOngoing ? (DateTimeOffset?)null : start.AddHours(2),
                detectedAt = start.AddHours(1),
                lastUnderproductionAt = start.AddHours(2),
                ongoing = AlarmIsOngoing,
                severity = "medium",
                ticketPriority = "Medium",
                medianRatio = 0.6,
                minRatio = 0.5,
                underproducingHours = 3,
                validHours = 3,
                durationHours = 3.0,
                expectedKwh = 3.0,
                actualKwh = 1.8,
                estimatedLostKwh = EstimatedLostKwh,
                reasonCode = "sustained_underproduction",
                reason = "Podvýroba 3 h",
                evidence = new[] { new { timestamp = start, actualPowerW = 600.0, expectedPowerW = 1000.0, ratio = 0.6 } },
            };
            return JsonSerializer.Serialize(new { alarms = new[] { alarm } });
        }
    }
}
