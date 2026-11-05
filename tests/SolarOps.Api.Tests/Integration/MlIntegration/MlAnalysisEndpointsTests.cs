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
using SolarOps.Api.Features.Production;
using SolarOps.Api.Features.Weather;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;
using InstallationsTestData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.MlIntegration;

/// <summary>
/// The analysis (#376) against a fake ML server: the real typed client talks to a stub HTTP handler, so the wire format,
/// the mapping of its failures and what is stored are all covered. The clock is fixed to 2026-06-21 12:30 UTC.
/// </summary>
public sealed class MlAnalysisEndpointsTests : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CurrentHour = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeMlServer _ml = new();
    private readonly MlApiFactory _factory;

    public MlAnalysisEndpointsTests(PostgresFixture postgres)
    {
        _factory = new MlApiFactory(postgres, _ml);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    private static string AnalysisUrl(Guid installationId) => $"/api/installations/{installationId}/analysis";

    private static string AlarmsUrl(Guid installationId) => $"/api/installations/{installationId}/underproduction-alarms";

    private static async Task<MlAnalysisResponse> RunAsync(HttpClient client, Guid installationId)
    {
        using var response = await client.PostAsync(AnalysisUrl(installationId), null);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<MlAnalysisResponse>();
    }

    private HttpClient Operator() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    [Fact]
    public async Task Analysis_Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.PostAsync(AnalysisUrl(Guid.CreateVersion7()), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Sales)]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Customer)]
    public async Task Analysis_RoleOutsideThePolicy_Returns403(string role)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        using var response = await client.PostAsync(AnalysisUrl(Guid.CreateVersion7()), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Alarms_Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync(AlarmsUrl(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Analysis_UnknownInstallation_Returns404_WithoutCallingTheService()
    {
        using var client = Operator();

        using var response = await client.PostAsync(AnalysisUrl(Guid.CreateVersion7()), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_ml.Requests);
    }

    [Fact]
    public async Task Analysis_WithoutDesignWithCoordinates_Returns409_WithoutCallingTheService()
    {
        var installationId = await SeedInstallationAsync(withDesign: false);
        using var client = Operator();

        using var response = await client.PostAsync(AnalysisUrl(installationId), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_ml.Requests);
    }

    [Fact]
    public async Task Analysis_WithoutForecastAndStoredPredictions_ReportsNothing_WithoutCallingTheService()
    {
        var installationId = await SeedInstallationAsync(withDesign: false);
        await SeedDesignAndWeatherAsync(installationId, forecastHours: 0);
        using var client = Operator();

        var result = await RunAsync(client, installationId);

        Assert.Null(result.ModelVersion);
        Assert.Equal(0, result.PredictedHours);
        Assert.Equal(0, result.Alarms);
        Assert.Empty(_ml.Requests);
    }

    [Fact]
    public async Task Analysis_SendsShiftedWeatherAndStoresPredictions_ThatTheComparisonThenShows()
    {
        var installationId = await SeedInstallationAsync();
        using var client = Operator();

        var result = await RunAsync(client, installationId);

        Assert.Equal(FakeMlServer.ModelVersion, result.ModelVersion);
        Assert.Equal(3, result.PredictedHours);
        Assert.Equal(Now, result.AnalyzedAt);

        using var request = JsonDocument.Parse(_ml.BodyOf("/v1/predictions/production"));
        Assert.Equal(CurrentHour.AddHours(-3), request.RootElement.GetProperty("weather")[0].GetProperty("timestamp").GetDateTimeOffset());
        var plant = request.RootElement.GetProperty("plant");
        Assert.Equal(48.15, plant.GetProperty("latitude").GetDouble());
        Assert.Equal(35, plant.GetProperty("tiltDeg").GetDouble());
        Assert.Equal(5, plant.GetProperty("dcCapacityKw").GetDouble());

        var comparison = await client.GetJsonAsync<ProductionComparisonResponse>(
            $"/api/installations/{installationId}/production/comparison?from=2026-06-21T09:00:00Z&to=2026-06-21T12:00:00Z");
        Assert.True(comparison.PredictionAvailable);
        Assert.Equal(FakeMlServer.ModelVersion, comparison.ModelVersion);
        Assert.Equal(3, comparison.PredictedEnergyKwh);
    }

    [Fact]
    public async Task Analysis_DetectionUsesOnlyFinishedHoursLabelledByTheirEnd()
    {
        var installationId = await SeedInstallationAsync();
        using var client = Operator();

        await RunAsync(client, installationId);

        using var request = JsonDocument.Parse(_ml.BodyOf("/v1/detections/underproduction"));
        Assert.Equal(5000, request.RootElement.GetProperty("ratedPowerW").GetDouble());
        var points = request.RootElement.GetProperty("points").EnumerateArray().ToList();
        Assert.Equal(3, points.Count);
        Assert.Equal(CurrentHour.AddHours(-2), points[0].GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal(CurrentHour, points[^1].GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, points[0].GetProperty("actualPowerW").ValueKind);
        Assert.Equal(500, points[0].GetProperty("ghiWm2").GetDouble());
    }

    [Fact]
    public async Task Analysis_RepeatedRun_OverwritesPredictionsAndUpdatesTheSameAlarm()
    {
        var installationId = await SeedInstallationAsync();
        using var client = Operator();

        await RunAsync(client, installationId);
        _ml.ExpectedEnergyWh = 2000;
        _ml.AlarmIsOngoing = false;
        await RunAsync(client, installationId);

        var stored = await _factory.ExecuteDbAsync(db => db.Set<StoredPrediction>().Where(p => p.InstallationId == installationId).ToListAsync());
        Assert.Equal(3, stored.Count);
        Assert.All(stored, p => Assert.Equal(2000, p.ExpectedEnergyWh));

        var alarms = await client.GetJsonAsync<UnderproductionAlarmListResponse>(AlarmsUrl(installationId));
        var alarm = Assert.Single(alarms.Items);
        Assert.False(alarm.Ongoing);
        Assert.NotNull(alarm.End);
    }

    [Fact]
    public async Task Analysis_MlServiceDown_Returns503_KeepsStoredDataAndTheRestOfTheApiWorks()
    {
        var installationId = await SeedInstallationAsync();
        using var client = Operator();
        await RunAsync(client, installationId);

        _ml.FailWith = HttpStatusCode.ServiceUnavailable;
        using var response = await client.PostAsync(AnalysisUrl(installationId), null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // The read side never calls the service: the last predictions and alarms stay available.
        var comparison = await client.GetJsonAsync<ProductionComparisonResponse>(
            $"/api/installations/{installationId}/production/comparison?from=2026-06-21T09:00:00Z&to=2026-06-21T12:00:00Z");
        Assert.True(comparison.PredictionAvailable);
        var alarms = await client.GetJsonAsync<UnderproductionAlarmListResponse>(AlarmsUrl(installationId));
        Assert.Single(alarms.Items);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Analysis_MlServiceRejectsTheRequest_Returns502_WithoutLeakingItsResponse(HttpStatusCode status)
    {
        var installationId = await SeedInstallationAsync();
        _ml.FailWith = status;
        using var client = Operator();

        using var response = await client.PostAsync(AnalysisUrl(installationId), null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain(FakeMlServer.SecretErrorText, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Alarms_CustomerSeesOwnInstallation_AndGets404ForAForeignOne()
    {
        var (ownUser, ownInstallation, foreignInstallation) = await SeedTwoCustomersAsync();
        using (var op = Operator())
        {
            await RunAsync(op, ownInstallation);
            await RunAsync(op, foreignInstallation);
        }

        using var customer = _factory.CreateApiClient(ownUser, Roles.Customer);

        var own = await customer.GetJsonAsync<UnderproductionAlarmListResponse>(AlarmsUrl(ownInstallation));
        var alarm = Assert.Single(own.Items);
        Assert.Equal(ownInstallation, alarm.InstallationId);
        Assert.Equal(UnderproductionSeverity.Medium, alarm.Severity);

        using var foreign = await customer.GetAsync(AlarmsUrl(foreignInstallation));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    private async Task<(Guid OwnUser, Guid OwnInstallation, Guid ForeignInstallation)> SeedTwoCustomersAsync()
    {
        var userA = await NewUserAsync();
        var userB = await NewUserAsync();
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var customerA = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("ml-a@example.sk") with { UserId = userA });
        var customerB = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("ml-b@example.sk", lastName: "Kováč") with { UserId = userB });

        using var op = Operator();
        var own = (await InstallationsTestData.CreateInstallationAsync(op, InstallationsTestData.NewInstallationRequest() with { CustomerId = customerA.Id })).Installation;
        var foreign = (await InstallationsTestData.CreateInstallationAsync(op, InstallationsTestData.NewInstallationRequest() with { CustomerId = customerB.Id })).Installation;
        await SeedDesignAndWeatherAsync(own.Id);
        await SeedDesignAndWeatherAsync(foreign.Id);
        return (userA, own.Id, foreign.Id);
    }

    private Task<Guid> NewUserAsync() =>
        _factory.ExecuteDbAsync(async db =>
        {
            var user = new AppUser { UserName = $"u-{Guid.NewGuid():N}@example.sk", Email = $"u-{Guid.NewGuid():N}@example.sk" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        });

    private async Task<Guid> SeedInstallationAsync(bool withDesign = true)
    {
        using var op = Operator();
        var installation = (await InstallationsTestData.CreateInstallationAsync(
            op,
            InstallationsTestData.NewInstallationRequest(plannedPowerKwp: 5m))).Installation;
        if (withDesign)
        {
            await SeedDesignAndWeatherAsync(installation.Id);
        }

        return installation.Id;
    }

    /// <summary>A design with coordinates and a forecast ending at the current hour: end labels 10:00, 11:00, 12:00 are hours 09, 10, 11.</summary>
    private Task SeedDesignAndWeatherAsync(Guid installationId, int forecastHours = 3) =>
        _factory.ExecuteDbAsync(async db =>
        {
            var installation = await db.Set<Installation>().AsNoTracking().SingleAsync(i => i.Id == installationId);
            var userId = Guid.CreateVersion7();
            var design = FveDesign.Request(installation.CustomerId, installation.SupplyPointId, null, userId, Now);
            design.RecordSiteSurvey("Strecha v poriadku", userId, Now);
            design.UpdateParameters(
                48.15,
                17.11,
                4500,
                ConsumptionProfile.Household,
                [
                    new RoofPlane { Name = "Sever", AzimuthDeg = 180, TiltDeg = 20, UsableAreaM2 = 5, ShadingLossPercent = 0, RoofType = RoofType.PitchedTile },
                    new RoofPlane { Name = "Juh", AzimuthDeg = 0, TiltDeg = 35, UsableAreaM2 = 30, ShadingLossPercent = 0, RoofType = RoofType.PitchedTile },
                ],
                Now);
            db.Add(design);

            for (var i = 0; i < forecastHours; i++)
            {
                db.Add(WeatherForecast.Create(
                    installationId,
                    new WeatherForecastPoint(CurrentHour.UtcDateTime.AddHours(i - 2), 500, 400, 100, 20, 25, 2),
                    48.15,
                    17.11,
                    Now.UtcDateTime));
            }

            await db.SaveChangesAsync();
            return true;
        });

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MlApiFactory(PostgresFixture postgres, FakeMlServer ml) : ApiFactory(postgres)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Ml:BaseUrl", "http://ml.test");
            builder.UseSetting("Ml:RetryDelaySeconds", "0");
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedClock(Now)));
                services.AddHttpClient<IMlServiceClient, MlServiceClient>().ConfigurePrimaryHttpMessageHandler(() => ml);
            });
        }
    }

    /// <summary>Answers like the ML service; one prediction per weather hour and, by default, one ongoing alarm.</summary>
    private sealed class FakeMlServer : HttpMessageHandler
    {
        public const string ModelVersion = "test-model-1";
        public const string SecretErrorText = "internal-ml-stack-trace";

        public List<(string Path, string Body)> Requests { get; } = [];

        public HttpStatusCode? FailWith { get; set; }

        public double ExpectedEnergyWh { get; set; } = 1000;

        public bool AlarmIsOngoing { get; set; } = true;

        public string BodyOf(string path) => Requests.First(r => r.Path == path).Body;

        // The stub outlives each request, so the base class must not dispose it with a test client.
        protected override void Dispose(bool disposing)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests)
            {
                Requests.Add((path, body));
            }

            if (FailWith is { } status)
            {
                return new HttpResponseMessage(status) { Content = new StringContent(SecretErrorText) };
            }

            using var document = JsonDocument.Parse(body);
            var json = path switch
            {
                "/v1/predictions/production" => Predictions(document.RootElement),
                "/v1/detections/underproduction" => Detections(document.RootElement),
                _ => throw new InvalidOperationException($"Unexpected ML request {path}"),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private string Predictions(JsonElement request)
        {
            var hours = request.GetProperty("weather").EnumerateArray().Select(hour =>
                new { timestamp = hour.GetProperty("timestamp").GetDateTimeOffset(), expectedPowerW = ExpectedEnergyWh, expectedEnergyWh = ExpectedEnergyWh });
            return JsonSerializer.Serialize(new { modelVersion = ModelVersion, generatedAt = Now, predictions = hours });
        }

        private string Detections(JsonElement request)
        {
            var installationId = request.GetProperty("installationId").GetGuid();
            var start = request.GetProperty("points")[0].GetProperty("timestamp").GetDateTimeOffset();
            var alarm = new
            {
                alarmId = DeterministicId(installationId),
                type = "underproduction",
                installationId,
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
                estimatedLostKwh = 1.2,
                reasonCode = "sustained_underproduction",
                reason = "Podvýroba 3 h",
                evidence = Array.Empty<object>(),
            };
            return JsonSerializer.Serialize(new { installationId, alarms = new[] { alarm } });
        }

        private static Guid DeterministicId(Guid installationId)
        {
            // Same id for the same installation and start, as the UUIDv5 of the real service.
            var bytes = installationId.ToByteArray();
            bytes[0] ^= 0x5A;
            return new Guid(bytes);
        }
    }
}
