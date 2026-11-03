using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.Installations;

/// <summary>Marks a checklist step done or not done (#90).</summary>
public sealed record UpdateChecklistStepRequest
{
    [Required]
    public bool? Done { get; init; }

    [MaxLength(InstallationChecklistItem.NoteMaxLength)]
    public string? Note { get; init; }
}

/// <summary>One step of the checklist: template data plus the stored state.</summary>
public sealed record ChecklistStepResponse(
    string Code,
    int Position,
    string Title,
    bool Required,
    bool Done,
    string? Note,
    Guid? ChangedBy,
    DateTimeOffset? ChangedAt);

/// <summary>Assembly checklist of an installation (#90). <c>Editable</c> is true only while mounting.</summary>
public sealed record ChecklistResponse(
    Guid InstallationId,
    bool Editable,
    bool Complete,
    IReadOnlyList<string> MissingRequiredSteps,
    IReadOnlyList<ChecklistStepResponse> Steps)
{
    internal static ChecklistResponse From(Installation installation) => new(
        installation.Id,
        installation.State == InstallationState.Mounting,
        installation.IsChecklistComplete,
        installation.MissingRequiredChecklistSteps,
        ChecklistTemplate.Steps
            .Select(step =>
            {
                var item = installation.ChecklistItems.FirstOrDefault(x => x.StepCode == step.Code);
                return new ChecklistStepResponse(
                    step.Code, step.Position, step.Title, step.Required, item?.Done ?? false, item?.Note, item?.ChangedBy, item?.ChangedAt);
            })
            .ToList());
}

/// <summary>Revision report with its file as base64 (#91). Allowed files: PDF, PNG, JPEG up to 5 MB.</summary>
public sealed record RecordRevisionReportRequest : IValidatableObject
{
    /// <summary>Day of the revision; must not be in the future.</summary>
    [Required]
    public DateOnly? InspectedOn { get; init; }

    /// <summary>Name of the revision technician (revízny technik).</summary>
    [Required, MaxLength(RevisionReport.InspectorNameMaxLength)]
    public string? InspectorName { get; init; }

    [MaxLength(RevisionReport.CertificateMaxLength)]
    public string? InspectorCertificateNumber { get; init; }

    [Required]
    public RevisionResult? Result { get; init; }

    /// <summary>Validity of the revision; required and after <see cref="InspectedOn"/> for a passed revision.</summary>
    public DateOnly? ValidUntil { get; init; }

    /// <summary>Defects found; required for a failed revision.</summary>
    [MaxLength(RevisionReport.FindingsMaxLength)]
    public string? Findings { get; init; }

    [Required, MaxLength(RevisionReport.FileNameMaxLength)]
    public string? FileName { get; init; }

    [Required, MaxLength(RevisionReport.ContentTypeMaxLength)]
    public string? ContentType { get; init; }

    [Required, MaxLength(RevisionReport.MaxFileBase64Length)]
    public string? ContentBase64 { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InspectorName is not null && string.IsNullOrWhiteSpace(InspectorName))
        {
            yield return new ValidationResult("The InspectorName field is required.", [nameof(InspectorName)]);
        }

        if (FileName is not null && string.IsNullOrWhiteSpace(FileName))
        {
            yield return new ValidationResult("The FileName field is required.", [nameof(FileName)]);
        }

        var time = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        if (InspectedOn is { } inspected && inspected > DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime))
        {
            yield return new ValidationResult("InspectedOn must not be in the future.", [nameof(InspectedOn)]);
        }

        if (Result == RevisionResult.Passed && (ValidUntil is null || (InspectedOn is { } from && ValidUntil <= from)))
        {
            yield return new ValidationResult("A passed revision needs ValidUntil after InspectedOn.", [nameof(ValidUntil)]);
        }

        if (Result == RevisionResult.Failed && string.IsNullOrWhiteSpace(Findings))
        {
            yield return new ValidationResult("A failed revision needs Findings (defects to repair).", [nameof(Findings)]);
        }

        if (ContentType is not null && !RevisionReport.AllowedContentTypes.ContainsKey(ContentType.Trim()))
        {
            yield return new ValidationResult(
                $"ContentType must be one of: {string.Join(", ", RevisionReport.AllowedContentTypes.Keys)}.", [nameof(ContentType)]);
        }
    }
}

/// <summary>Revision report metadata; the file itself is downloaded separately.</summary>
public sealed record RevisionReportResponse(
    Guid Id,
    Guid InstallationId,
    DateOnly InspectedOn,
    string InspectorName,
    string? InspectorCertificateNumber,
    RevisionResult Result,
    DateOnly? ValidUntil,
    string? Findings,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    Guid RecordedBy,
    DateTimeOffset RecordedAt)
{
    internal static RevisionReportResponse From(RevisionReport x) => new(
        x.Id, x.InstallationId, x.InspectedOn, x.InspectorName, x.InspectorCertificateNumber, x.Result, x.ValidUntil, x.Findings,
        x.FileName, x.ContentType, x.SizeBytes, x.Sha256, x.RecordedBy, x.RecordedAt);
}

/// <summary>Acceptance protocol (preberací protokol) of commissioning (#92).</summary>
public sealed record CommissionInstallationRequest : IValidatableObject
{
    /// <summary>Day the plant was put into operation; must not be in the future.</summary>
    [Required]
    public DateOnly? CommissionedOn { get; init; }

    /// <summary>Person who took the plant over on the customer's side.</summary>
    [Required, MaxLength(Installation.AcceptedByMaxLength)]
    public string? AcceptedBy { get; init; }

    [MaxLength(Installation.NoteMaxLength)]
    public string? Note { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AcceptedBy is not null && string.IsNullOrWhiteSpace(AcceptedBy))
        {
            yield return new ValidationResult("The AcceptedBy field is required.", [nameof(AcceptedBy)]);
        }

        var time = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        if (CommissionedOn is { } day && day > DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime))
        {
            yield return new ValidationResult("CommissionedOn must not be in the future.", [nameof(CommissionedOn)]);
        }
    }
}

/// <summary>Commissioning data printed in the hand-over protocol.</summary>
public sealed record CommissioningProtocolResponse(
    DateOnly CommissionedOn,
    string AcceptedBy,
    string? Note,
    Guid CommissionedBy,
    bool MonitoringEnabled,
    DateTimeOffset? MonitoringEnabledAt);

/// <summary>Installed device as listed in the documentation package.</summary>
public sealed record DocumentationDeviceResponse(DeviceType Type, string SerialNumber, string Manufacturer, string Model, int PowerW);

/// <summary>One recorded download of a document.</summary>
public sealed record DocumentDownloadResponse(InstallationDocumentKind Kind, Guid? DocumentId, Guid DownloadedBy, DateTimeOffset DownloadedAt);

/// <summary>Documentation package for the customer (#93): revision, acceptance protocol, devices and download log.</summary>
public sealed record InstallationDocumentationResponse(
    Guid InstallationId,
    string InstallationName,
    InstallationState State,
    CommissioningProtocolResponse Protocol,
    RevisionReportResponse RevisionReport,
    IReadOnlyList<DocumentationDeviceResponse> Devices,
    IReadOnlyList<DocumentDownloadResponse> Downloads);

/// <summary>User with role Technician that a planner can assign (#89).</summary>
public sealed record TechnicianResponse(Guid Id, string? UserName, string? Email);
