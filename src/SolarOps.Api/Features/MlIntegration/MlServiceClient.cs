using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>A call to the ML service failed. The details stay in the log; API clients get a generic problem response.</summary>
/// <param name="message">Internal description.</param>
/// <param name="isUnavailable">True when the service cannot be reached (503); false when it answered unusably (502).</param>
/// <param name="innerException">Original failure, if any.</param>
public sealed class MlServiceException(string message, bool isUnavailable, Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool IsUnavailable { get; } = isUnavailable;
}

/// <summary>Typed client of the ML service (ADR #196).</summary>
internal interface IMlServiceClient
{
    Task<MlPredictionResponse> PredictAsync(MlPredictionRequest request, CancellationToken ct);

    Task<MlDetectionResponse> DetectAsync(MlDetectionRequest request, CancellationToken ct);
}

/// <summary>
/// Calls the service with a timeout and one retry of what a cold start causes (timeout, connection failure, 502, 503, 504).
/// Other answers are not retried because the same request would fail again. Every failure is a <see cref="MlServiceException"/>,
/// only a cancellation by the caller passes through.
/// </summary>
internal sealed class MlServiceClient(HttpClient http, IOptions<MlOptions> options, ILogger<MlServiceClient> logger) : IMlServiceClient
{
    private const string PredictionPath = "v1/predictions/production";
    private const string DetectionPath = "v1/detections/underproduction";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Task<MlPredictionResponse> PredictAsync(MlPredictionRequest request, CancellationToken ct) =>
        PostAsync<MlPredictionRequest, MlPredictionResponse>(PredictionPath, request, ct);

    public Task<MlDetectionResponse> DetectAsync(MlDetectionRequest request, CancellationToken ct) =>
        PostAsync<MlDetectionRequest, MlDetectionResponse>(DetectionPath, request, ct);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            throw new MlServiceException("The ML service address (Ml:BaseUrl) is not configured.", isUnavailable: true);
        }

        var uri = new Uri($"{settings.BaseUrl.TrimEnd('/')}/{path}");
        var maxAttempts = Math.Max(1, settings.MaxAttempts);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendAsync<TRequest, TResponse>(uri, request, ct);
            }
            catch (MlServiceException ex) when (ex.IsUnavailable && attempt < maxAttempts)
            {
                logger.LogWarning(ex, "ML service call {Path} failed (attempt {Attempt}/{MaxAttempts}); retrying", path, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, settings.RetryDelaySeconds)), ct);
            }
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(Uri uri, TRequest request, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(uri, request, Json, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new MlServiceException(
                    $"ML service {uri.AbsolutePath} returned {(int)response.StatusCode}: {body}",
                    isUnavailable: response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout);
            }

            return await response.Content.ReadFromJsonAsync<TResponse>(Json, ct)
                ?? throw new MlServiceException($"ML service {uri.AbsolutePath} returned an empty response.", isUnavailable: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new MlServiceException($"ML service {uri.AbsolutePath} timed out.", isUnavailable: true, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MlServiceException($"ML service {uri.AbsolutePath} cannot be reached.", isUnavailable: true, ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new MlServiceException($"ML service {uri.AbsolutePath} returned an invalid response.", isUnavailable: false, ex);
        }
    }
}
