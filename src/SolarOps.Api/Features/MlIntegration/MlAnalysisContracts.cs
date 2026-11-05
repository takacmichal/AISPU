using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>Outcome of one analysis of an installation (#376).</summary>
/// <param name="InstallationId">Analysed installation.</param>
/// <param name="ModelVersion">Model of the new predictions; null when there was no usable forecast to predict from.</param>
/// <param name="PredictedHours">Number of hours predicted and stored in this run.</param>
/// <param name="Alarms">Number of alarms the detection reported in this run.</param>
/// <param name="OngoingAlarms">How many of them are still ongoing.</param>
/// <param name="AnalyzedAt">Time of the run, UTC.</param>
public sealed record MlAnalysisResponse(
    Guid InstallationId,
    string? ModelVersion,
    int PredictedHours,
    int Alarms,
    int OngoingAlarms,
    DateTimeOffset AnalyzedAt);

/// <summary>Underproduction alarms of an installation, newest first, at most 100.</summary>
public sealed record UnderproductionAlarmListResponse(IReadOnlyList<UnderproductionAlarmResponse> Items);

/// <summary>Underproduction event (ADR #194). Hours are labelled by their end, as the detection reports them.</summary>
/// <param name="Id">Stable for the same installation and start.</param>
/// <param name="InstallationId">Installation of the alarm.</param>
/// <param name="Start">End of the first underproducing hour, UTC.</param>
/// <param name="End">End of the event; null while it is ongoing.</param>
/// <param name="DetectedAt">End of the hour that confirmed the event.</param>
/// <param name="LastUnderproductionAt">End of the last underproducing hour.</param>
/// <param name="Ongoing">True until production recovers.</param>
/// <param name="Severity">Severity by the ratio and duration.</param>
/// <param name="TicketPriority">Recommended priority of a service ticket.</param>
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
public sealed record UnderproductionAlarmResponse(
    Guid Id,
    Guid InstallationId,
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
    DateTimeOffset UpdatedAt)
{
    public static UnderproductionAlarmResponse From(UnderproductionAlarm a) => new(
        a.Id,
        a.InstallationId,
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
        a.UpdatedAt);
}
