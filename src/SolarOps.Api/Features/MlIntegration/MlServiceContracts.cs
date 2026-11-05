using System.Text.Json.Serialization;
using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Features.MlIntegration;

// Wire format of the ML service (ml/src/solarops_ml/prediction/service.py and detection/api.py). Internal: API clients
// never see these types. Nulls are omitted on the wire (see MlServiceClient) unless a field below says otherwise.

// InstallationId: Echoed by the service for tracing.
// Plant: Parameters of the installation.
// Weather: Hourly weather, strictly increasing, at most 168 hours.
internal sealed record MlPredictionRequest(Guid InstallationId, MlPlant Plant, IReadOnlyList<MlWeatherHour> Weather);

// AzimuthDeg: PVGIS convention as in RoofPlane: 0 = south, -90 = east, 90 = west.
// DcAcRatio: Null is omitted; the service then uses its default ratio.
internal sealed record MlPlant(double Latitude, double Longitude, double DcCapacityKw, double TiltDeg, double AzimuthDeg, double? DcAcRatio);

// Timestamp: Start of the hour, UTC; the row describes [t, t + 1 h).
internal sealed record MlWeatherHour(
    DateTimeOffset Timestamp,
    double GhiWm2,
    double DniWm2,
    double DhiWm2,
    double? CloudCoverPct,
    double TemperatureC,
    double? WindSpeedMs);

internal sealed record MlPredictionResponse(string ModelVersion, DateTimeOffset GeneratedAt, IReadOnlyList<MlPredictedHour> Predictions);

// Timestamp: Start of the hour, UTC.
internal sealed record MlPredictedHour(DateTimeOffset Timestamp, double ExpectedPowerW, double ExpectedEnergyWh);

// RatedPowerW: Nominal power, base of the 5 % floor of the expected power.
// AcLimitW: Inverter AC limit; null is omitted and the expected power is then not clipped.
internal sealed record MlDetectionRequest(Guid InstallationId, double RatedPowerW, double? AcLimitW, IReadOnlyList<MlDetectionPoint> Points);

// Timestamp: END of the hour, UTC (unlike predictions and weather).
// ActualPowerW: Mean AC power in the hour. Required by the service even when null, which means no data (an outage, not underproduction).
// ExpectedPowerW: Predicted mean AC power in the hour; required like ActualPowerW.
// GhiWm2: Irradiance of the hour; hours below the minimum are excluded by the rule of ADR #194.
// CloudCoverPct: Heavy cloud lowers the sensitivity of the rule.
// SampleCoverage: Share of the telemetry samples received, 0 to 1.
internal sealed record MlDetectionPoint(
    DateTimeOffset Timestamp,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? ActualPowerW,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? ExpectedPowerW,
    double? GhiWm2,
    double? CloudCoverPct,
    double SampleCoverage);

internal sealed record MlDetectionResponse(IReadOnlyList<MlAlarm> Alarms);

// AlarmId: UUIDv5 of the installation and the start: stable across repeated calls over overlapping windows.
// Start: End label of the first underproducing hour.
// End: Null while the alarm is ongoing.
internal sealed record MlAlarm(
    Guid AlarmId,
    DateTimeOffset Start,
    DateTimeOffset? End,
    DateTimeOffset DetectedAt,
    DateTimeOffset LastUnderproductionAt,
    bool Ongoing,
    UnderproductionSeverity Severity,
    ServiceTicketPriority TicketPriority,
    double MedianRatio,
    double MinRatio,
    int UnderproducingHours,
    int ValidHours,
    double DurationHours,
    double ExpectedKwh,
    double ActualKwh,
    double EstimatedLostKwh,
    string ReasonCode,
    string Reason);
