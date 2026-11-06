using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>Outcome of one analysis of an installation (#376).</summary>
/// <param name="InstallationId">Analysed installation.</param>
/// <param name="ModelVersion">Model of the new predictions; null when there was no usable forecast to predict from.</param>
/// <param name="PredictedHours">Number of hours predicted and stored in this run.</param>
/// <param name="Alarms">Number of alarms the detection reported in this run.</param>
/// <param name="OngoingAlarms">How many of them are still ongoing.</param>
/// <param name="TicketsCreated">Service tickets created from alarms in this run (#33).</param>
/// <param name="TicketsSupplemented">Alarms recorded on an already open ticket of the installation instead of a duplicate.</param>
/// <param name="AnalyzedAt">Time of the run, UTC.</param>
public sealed record MlAnalysisResponse(
    Guid InstallationId,
    string? ModelVersion,
    int PredictedHours,
    int Alarms,
    int OngoingAlarms,
    int TicketsCreated,
    int TicketsSupplemented,
    DateTimeOffset AnalyzedAt);

/// <summary>Outcome of a prediction for a past period (backfilled weather); no detection runs and no alarm or ticket is created.</summary>
/// <param name="InstallationId">Installation of the predictions.</param>
/// <param name="From">First day of the period, UTC.</param>
/// <param name="To">Last day of the period, UTC (inclusive).</param>
/// <param name="ModelVersion">Model of the predictions; null when nothing was predicted.</param>
/// <param name="WeatherHours">Hours of stored weather the prediction used.</param>
/// <param name="PredictedHours">Hours predicted and stored in this run.</param>
public sealed record PastPredictionResponse(
    Guid InstallationId,
    DateOnly From,
    DateOnly To,
    string? ModelVersion,
    int WeatherHours,
    int PredictedHours);

/// <summary>Underproduction alarms of an installation, newest first, at most 100.</summary>
public sealed record UnderproductionAlarmListResponse(IReadOnlyList<UnderproductionAlarmResponse> Items);

/// <summary>Underproduction event (ADR #194). Hours are labelled by their end, as the detection reports them.</summary>
/// <param name="Id">Stable for the same installation and start.</param>
/// <param name="InstallationId">Installation of the alarm.</param>
/// <param name="InstallationName">Name of the installation.</param>
/// <param name="Start">End of the first underproducing hour, UTC.</param>
/// <param name="End">End of the event; null while it is ongoing.</param>
/// <param name="DetectedAt">End of the hour that confirmed the event.</param>
/// <param name="LastUnderproductionAt">End of the last underproducing hour.</param>
/// <param name="Ongoing">True until production recovers.</param>
/// <param name="Severity">Severity by the ratio and duration.</param>
/// <param name="TicketPriority">Priority of the service ticket: by the detection, raised by a large loss.</param>
/// <param name="MedianRatio">Median of actual / expected power in the event.</param>
/// <param name="MinRatio">Lowest actual / expected power in the event.</param>
/// <param name="UnderproducingHours">Hours below the threshold.</param>
/// <param name="ValidHours">Hours the rule could evaluate.</param>
/// <param name="DurationHours">Length of the event.</param>
/// <param name="ExpectedKwh">Expected energy in the event.</param>
/// <param name="ActualKwh">Produced energy in the event.</param>
/// <param name="EstimatedLostKwh">Estimated loss.</param>
/// <param name="ReasonCode">Machine-readable reason.</param>
/// <param name="Reason">Reason for people.</param>
/// <param name="UpdatedAt">Last time an analysis reported this alarm.</param>
/// <param name="TicketId">Service ticket handling the alarm; null when it has none (an ended event gets none).</param>
public sealed record UnderproductionAlarmResponse(
    Guid Id,
    Guid InstallationId,
    string InstallationName,
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
    string Reason,
    DateTimeOffset UpdatedAt,
    Guid? TicketId)
{
    public static UnderproductionAlarmResponse From(UnderproductionAlarm a, string installationName) => new(
        a.Id,
        a.InstallationId,
        installationName,
        a.Start,
        a.End,
        a.DetectedAt,
        a.LastUnderproductionAt,
        a.Ongoing,
        a.Severity,
        a.TicketPriority,
        a.MedianRatio,
        a.MinRatio,
        a.UnderproducingHours,
        a.ValidHours,
        a.DurationHours,
        a.ExpectedKwh,
        a.ActualKwh,
        a.EstimatedLostKwh,
        a.ReasonCode,
        a.Reason,
        a.UpdatedAt,
        a.TicketId);
}
