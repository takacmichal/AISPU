using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SolarOps.Api.Features.MlIntegration;
using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Tests.Unit.MlIntegration;

public sealed class MlServiceClientTests
{
    private static readonly DateTimeOffset Hour = new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid InstallationId = Guid.Parse("7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10");

    private static readonly MlPredictionRequest Prediction = new(
        InstallationId,
        new MlPlant(48.15, 17.11, 8.4, 30, 0, 1.2),
        [new MlWeatherHour(Hour, 780, 640, 120, 20, 24.5, 2.1)]);

    private static readonly MlDetectionRequest Detection = new(
        InstallationId,
        8400,
        7000,
        [new MlDetectionPoint(Hour, null, 2000, 500, 20, 0)]);

    private const string PredictionBody = """
        {"modelVersion":"physical-baseline-0.1","generatedAt":"2026-06-21T08:00:03Z",
         "predictions":[{"timestamp":"2026-06-21T10:00:00Z","expectedPowerW":6120.0,"expectedEnergyWh":6120.0}]}
        """;

    private const string DetectionBody = """
        {"installationId":"7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10","parameters":{"ratioThreshold":0.85},
         "summary":{"hours":3,"validHours":3,"underproducingHours":3,"ambiguousHours":0,"reducedSensitivityHours":0,"excludedHours":{},"alarms":1},
         "alarms":[{"alarmId":"11111111-1111-5111-8111-111111111111","type":"underproduction",
           "installationId":"7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10","start":"2026-06-21T10:00:00Z","end":null,
           "detectedAt":"2026-06-21T11:00:00Z","lastUnderproductionAt":"2026-06-21T12:00:00Z","ongoing":true,
           "severity":"medium","ticketPriority":"Medium","medianRatio":0.6,"minRatio":0.5,"underproducingHours":3,"validHours":3,
           "durationHours":3.0,"expectedKwh":6.0,"actualKwh":3.6,"estimatedLostKwh":2.4,"reasonCode":"sustained_underproduction",
           "reason":"Podvýroba","evidence":[]}],
         "hours":null}
        """;

    [Fact]
    public async Task PredictAsync_PostsCamelCaseContractAndParsesResponse()
    {
        var handler = new StubHandler(_ => Json(PredictionBody));

        var result = await Client(handler).PredictAsync(Prediction, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://ml.test/v1/predictions/production", request.Uri.ToString());
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(8.4, body.RootElement.GetProperty("plant").GetProperty("dcCapacityKw").GetDouble());
        Assert.Equal(1.2, body.RootElement.GetProperty("plant").GetProperty("dcAcRatio").GetDouble());
        var weather = body.RootElement.GetProperty("weather")[0];
        Assert.Equal(780, weather.GetProperty("ghiWm2").GetDouble());
        Assert.Equal(24.5, weather.GetProperty("temperatureC").GetDouble());
        Assert.Equal(Hour, weather.GetProperty("timestamp").GetDateTimeOffset());

        Assert.Equal("physical-baseline-0.1", result.ModelVersion);
        var hour = Assert.Single(result.Predictions);
        Assert.Equal(Hour, hour.Timestamp);
        Assert.Equal(6120, hour.ExpectedEnergyWh);
    }

    [Fact]
    public async Task PredictAsync_OmitsAnUnknownDcAcRatio_TheServiceThenUsesItsDefault()
    {
        var handler = new StubHandler(_ => Json(PredictionBody));
        var request = Prediction with { Plant = Prediction.Plant with { DcAcRatio = null } };

        await Client(handler).PredictAsync(request, CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.False(body.RootElement.GetProperty("plant").TryGetProperty("dcAcRatio", out _));
    }

    [Fact]
    public async Task DetectAsync_SendsNullActualPower_BecauseTheFieldIsRequired()
    {
        var handler = new StubHandler(_ => Json(DetectionBody));

        await Client(handler).DetectAsync(Detection, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://ml.test/v1/detections/underproduction", request.Uri.ToString());
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(8400, body.RootElement.GetProperty("ratedPowerW").GetDouble());
        Assert.Equal(7000, body.RootElement.GetProperty("acLimitW").GetDouble());
        var point = body.RootElement.GetProperty("points")[0];
        Assert.Equal(JsonValueKind.Null, point.GetProperty("actualPowerW").ValueKind);
        Assert.Equal(2000, point.GetProperty("expectedPowerW").GetDouble());
        Assert.Equal(0, point.GetProperty("sampleCoverage").GetDouble());
    }

    [Fact]
    public async Task DetectAsync_ParsesAlarm()
    {
        var result = await Client(new StubHandler(_ => Json(DetectionBody))).DetectAsync(Detection, CancellationToken.None);

        var alarm = Assert.Single(result.Alarms);
        Assert.Equal(Guid.Parse("11111111-1111-5111-8111-111111111111"), alarm.AlarmId);
        Assert.True(alarm.Ongoing);
        Assert.Null(alarm.End);
        Assert.Equal(UnderproductionSeverity.Medium, alarm.Severity);
        Assert.Equal(ServiceTicketPriority.Medium, alarm.TicketPriority);
        Assert.Equal(2.4, alarm.EstimatedLostKwh);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task TransientStatus_IsRetriedOnce_ThenSucceeds(HttpStatusCode status)
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1 ? new HttpResponseMessage(status) : Json(PredictionBody));

        var result = await Client(handler).PredictAsync(Prediction, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("physical-baseline-0.1", result.ModelVersion);
    }

    [Fact]
    public async Task TransientStatus_AfterAllAttempts_IsReportedAsUnavailable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var error = await Assert.ThrowsAsync<MlServiceException>(() => Client(handler).PredictAsync(Prediction, CancellationToken.None));

        Assert.True(error.IsUnavailable);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task OtherErrorStatus_IsNotRetried_AndIsNotUnavailable(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        var error = await Assert.ThrowsAsync<MlServiceException>(() => Client(handler).DetectAsync(Detection, CancellationToken.None));

        Assert.False(error.IsUnavailable);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ConnectionFailure_IsReportedAsUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var error = await Assert.ThrowsAsync<MlServiceException>(() => Client(handler).PredictAsync(Prediction, CancellationToken.None));

        Assert.True(error.IsUnavailable);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Timeout_IsReportedAsUnavailable()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));

        var error = await Assert.ThrowsAsync<MlServiceException>(() => Client(handler).PredictAsync(Prediction, CancellationToken.None));

        Assert.True(error.IsUnavailable);
    }

    [Fact]
    public async Task CancelledByCaller_IsNotTranslatedAndNotRetried()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHandler(_ =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).PredictAsync(Prediction, cancellation.Token));

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"modelVersion":"x"}""")]
    public async Task InvalidResponse_IsAnError_NotACrash(string body)
    {
        var error = await Assert.ThrowsAsync<MlServiceException>(
            () => Client(new StubHandler(_ => Json(body))).PredictAsync(Prediction, CancellationToken.None));

        Assert.False(error.IsUnavailable);
    }

    [Fact]
    public async Task UnknownSeverity_IsAnError_NotACrash()
    {
        var body = DetectionBody.Replace("\"severity\":\"medium\"", "\"severity\":\"catastrophic\"", StringComparison.Ordinal);

        var error = await Assert.ThrowsAsync<MlServiceException>(
            () => Client(new StubHandler(_ => Json(body))).DetectAsync(Detection, CancellationToken.None));

        Assert.False(error.IsUnavailable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingBaseUrl_IsUnavailable_WithoutAnyCall(string baseUrl)
    {
        var handler = new StubHandler(_ => Json(PredictionBody));

        var error = await Assert.ThrowsAsync<MlServiceException>(
            () => Client(handler, baseUrl).PredictAsync(Prediction, CancellationToken.None));

        Assert.True(error.IsUnavailable);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BaseUrlWithTrailingSlash_StillBuildsTheRightAddress()
    {
        var handler = new StubHandler(_ => Json(PredictionBody));

        await Client(handler, "http://ml.test/").PredictAsync(Prediction, CancellationToken.None);

        Assert.Equal("http://ml.test/v1/predictions/production", Assert.Single(handler.Requests).Uri.ToString());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("http://ml:8000", true)]
    [InlineData("https://ml.example.com/", true)]
    [InlineData("ml:8000", false)]
    [InlineData("ftp://ml", false)]
    [InlineData("not a url", false)]
    public void BaseUrl_IsValidatedAtStartup(string baseUrl, bool valid)
    {
        Assert.Equal(valid, new MlOptions { BaseUrl = baseUrl }.HasValidBaseUrl());
    }

    private static MlServiceClient Client(StubHandler handler, string baseUrl = "http://ml.test") => new(
        new HttpClient(handler),
        Options.Create(new MlOptions { BaseUrl = baseUrl, MaxAttempts = 2, RetryDelaySeconds = 0 }),
        NullLogger<MlServiceClient>.Instance);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(Uri Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!, body));
            return respond(request);
        }
    }
}
