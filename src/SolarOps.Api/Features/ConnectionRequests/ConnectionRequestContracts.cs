using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>
/// Metadata of one attachment, optionally with the file content. When <see cref="ContentBase64"/> is empty, only
/// metadata is stored (legacy clients); when it is set, the decoded file can be downloaded from the API.
/// </summary>
public sealed record AttachmentRequest
{
    [Required]
    public ConnectionRequestAttachmentKind? Kind { get; init; }

    [Required, MaxLength(ConnectionRequest.FileNameMaxLength)]
    public string? FileName { get; init; }

    /// <summary>Media type of the file, for example <c>application/pdf</c>; validated, parameters are not allowed.</summary>
    [Required, MaxLength(ConnectionRequest.ContentTypeMaxLength)]
    public string? ContentType { get; init; }

    /// <summary>File size in bytes; may be 0 when <see cref="ContentBase64"/> is sent (the server derives it).</summary>
    [Range(0, long.MaxValue)]
    public long SizeBytes { get; init; }

    /// <summary>Base64 content of the file; at most <see cref="ConnectionRequest.MaxAttachmentBytes"/> before encoding.</summary>
    [MaxLength(ConnectionRequest.MaxAttachmentBase64Length)]
    public string? ContentBase64 { get; init; }
}

/// <summary>Payload for creating a connection request (state <c>Draft</c>).</summary>
public sealed record CreateConnectionRequestRequest : IValidatableObject
{
    /// <summary>Installation (module Installations) the request belongs to.</summary>
    [Required]
    public Guid? InstallationId { get; init; }

    /// <summary>EIC code of the supply point.</summary>
    [Required, MaxLength(ConnectionRequest.EicMaxLength)]
    public string? EicCode { get; init; }

    /// <summary>Total rated AC power of the inverters in kW; above 10.8 kW the request is a large source.</summary>
    [Range(0.01, 100000)]
    public decimal RatedAcPowerKw { get; init; }

    /// <summary>Main breaker of the supply point in amperes per phase.</summary>
    [Range(1, 1000)]
    public int? MainBreakerAmps { get; init; }

    /// <summary>Must contain at least a <c>SingleLineDiagram</c> and an <c>OwnerConsent</c>.</summary>
    [Required, MaxLength(ConnectionRequest.MaxAttachments)]
    public IReadOnlyList<AttachmentRequest>? Attachments { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InstallationId == Guid.Empty)
        {
            yield return new ValidationResult("The InstallationId field must not be an empty id.", [nameof(InstallationId)]);
        }

        foreach (var result in ConnectionRequestValidation.ValidateContent(
                     validationContext, EicCode, Attachments, nameof(Attachments)))
        {
            yield return result;
        }
    }
}

/// <summary>Payload for updating a draft connection request; the installation stays the same.</summary>
public sealed record UpdateConnectionRequestRequest : IValidatableObject
{
    [Required, MaxLength(ConnectionRequest.EicMaxLength)]
    public string? EicCode { get; init; }

    [Range(0.01, 100000)]
    public decimal RatedAcPowerKw { get; init; }

    [Range(1, 1000)]
    public int? MainBreakerAmps { get; init; }

    [Required, MaxLength(ConnectionRequest.MaxAttachments)]
    public IReadOnlyList<AttachmentRequest>? Attachments { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ConnectionRequestValidation.ValidateContent(
                     validationContext, EicCode, Attachments, nameof(Attachments)))
        {
            yield return result;
        }
    }
}

/// <summary>Answer of the (simulated) distributor to a submitted request.</summary>
public enum DistributorDecision
{
    /// <summary>Approved: a contract is offered (state ContractOffered).</summary>
    Approve,

    /// <summary>The request must be completed (state InfoRequested); a note is required.</summary>
    RequestInfo,

    /// <summary>The request is refused (state Rejected); a note with the reason is required.</summary>
    Reject,
}

/// <summary>Payload of the distributor simulator: the decision and, for <c>RequestInfo</c> and <c>Reject</c>, a note.</summary>
public sealed record DistributorDecisionRequest : IValidatableObject
{
    [Required]
    public DistributorDecision? Decision { get; init; }

    /// <summary>What is missing or why the request was refused; required for <c>RequestInfo</c> and <c>Reject</c>.</summary>
    [MaxLength(ConnectionRequest.DecisionNoteMaxLength)]
    public string? Note { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Decision is DistributorDecision.RequestInfo or DistributorDecision.Reject && string.IsNullOrWhiteSpace(Note))
        {
            yield return new ValidationResult($"The Note field is required for the decision {Decision}.", [nameof(Note)]);
        }
    }
}

internal static class ConnectionRequestValidation
{
    internal static IEnumerable<ValidationResult> ValidateContent(
        ValidationContext context,
        string? eicCode,
        IReadOnlyList<AttachmentRequest>? attachments,
        string attachmentsMember)
    {
        if (eicCode is not null && string.IsNullOrWhiteSpace(eicCode))
        {
            yield return new ValidationResult("The EicCode field is required.", [nameof(CreateConnectionRequestRequest.EicCode)]);
        }

        if (attachments is null)
        {
            yield break;
        }

        if (attachments.Count > ConnectionRequest.MaxAttachments)
        {
            yield return new ValidationResult(
                $"At most {ConnectionRequest.MaxAttachments} attachments can be sent with a connection request.",
                [attachmentsMember]);
        }

        // The validation filter checks top-level members only, so items are checked here.
        foreach (var (attachment, index) in attachments.Select((a, i) => (a, i)))
        {
            if (attachment is null)
            {
                yield return new ValidationResult("Attachment must not be null.", [$"{attachmentsMember}[{index}]"]);
                continue;
            }

            var errors = DataAnnotationsValidator.Validate(attachment, context);
            foreach (var (member, messages) in errors)
            {
                foreach (var message in messages)
                {
                    yield return new ValidationResult(message, [$"{attachmentsMember}[{index}].{member}"]);
                }
            }

            if (string.IsNullOrWhiteSpace(attachment.ContentBase64) && attachment.SizeBytes <= 0)
            {
                yield return new ValidationResult(
                    "SizeBytes must be greater than 0 when no file content is sent.",
                    [$"{attachmentsMember}[{index}].SizeBytes"]);
            }

            // The value is echoed back in the Content-Type header of the download endpoint, so it must be a media type.
            if (!string.IsNullOrWhiteSpace(attachment.ContentType) && !IsMediaType(attachment.ContentType))
            {
                yield return new ValidationResult(
                    "The ContentType field must be a media type such as application/pdf, without parameters.",
                    [$"{attachmentsMember}[{index}].ContentType"]);
            }
        }

        var missing = ConnectionRequest.RequiredAttachmentKinds
            .Where(k => attachments.All(a => a.Kind != k))
            .ToList();
        if (missing.Count > 0)
        {
            yield return new ValidationResult(
                $"Missing required attachments: {string.Join(", ", missing)}.",
                [attachmentsMember]);
        }
    }

    /// <summary>
    /// True for a bare media type such as <c>application/pdf</c>: no parameters, no wildcards. Garbage would otherwise
    /// either break the download response or smuggle something into the <c>Content-Type</c> header.
    /// </summary>
    private static bool IsMediaType(string value) =>
        MediaTypeHeaderValue.TryParse(value, out var mediaType)
        && mediaType.Parameters.Count == 0
        && mediaType.MediaType is { } type
        && !type.Contains('*')
        && string.Equals(type, value.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Attachment metadata as returned by the API.</summary>
public sealed record AttachmentResponse(
    Guid Id,
    ConnectionRequestAttachmentKind Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    bool HasContent,
    string? DownloadUrl)
{
    internal static AttachmentResponse From(Guid requestId, ConnectionRequestAttachment attachment) => new(
        attachment.Id,
        attachment.Kind,
        attachment.FileName,
        attachment.ContentType,
        attachment.SizeBytes,
        attachment.CreatedAt,
        attachment.HasContent,
        attachment.HasContent ? BuildDownloadUrl(requestId, attachment.Id) : null);

    internal static AttachmentResponse FromMetadata(
        Guid requestId,
        Guid id,
        ConnectionRequestAttachmentKind kind,
        string fileName,
        string contentType,
        long sizeBytes,
        DateTimeOffset createdAt,
        bool hasContent) => new(
        id,
        kind,
        fileName,
        contentType,
        sizeBytes,
        createdAt,
        hasContent,
        hasContent ? BuildDownloadUrl(requestId, id) : null);

    private static string BuildDownloadUrl(Guid requestId, Guid attachmentId) =>
        $"/api/connection-requests/{requestId}/attachments/{attachmentId}";
}

/// <summary>
/// Connection request with its attachments and deadline. <c>ProposalRemainingDays</c> (calendar days, negative when late) and
/// <c>ProposalOverdue</c> are set only while the request waits for the distributor (state <c>Submitted</c>).
/// </summary>
public sealed record ConnectionRequestResponse(
    Guid Id,
    Guid InstallationId,
    Guid CustomerId,
    Guid SupplyPointId,
    string EicCode,
    decimal RatedAcPowerKw,
    int? MainBreakerAmps,
    ConnectionRequestType Type,
    ConnectionRequestState State,
    IReadOnlyList<AttachmentResponse> Attachments,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    DateOnly? ProposalDueDate,
    int? ProposalRemainingDays,
    bool ProposalOverdue,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    DateTimeOffset? ConditionsReviewedAt,
    DateTimeOffset? ContractSentAt)
{
    internal static ConnectionRequestResponse From(ConnectionRequest x, DateOnly today) => From(
        x,
        x.Attachments
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => AttachmentResponse.From(x.Id, a))
            .ToList(),
        today);

    internal static ConnectionRequestResponse From(
        ConnectionRequest x,
        IReadOnlyList<AttachmentResponse> attachments,
        DateOnly today)
    {
        var deadline = ConnectionRequestDeadlines.Evaluate(x.State, x.ProposalDueDate, today);
        return new(
            x.Id, x.InstallationId, x.CustomerId, x.SupplyPointId, x.EicCode, x.RatedAcPowerKw, x.MainBreakerAmps, x.Type, x.State,
            attachments,
            x.CreatedAt, x.UpdatedAt,
            x.SubmittedAt, x.ProposalDueDate, deadline.RemainingDays, deadline.IsOverdue, x.DecidedAt, x.DecisionNote, x.ConditionsReviewedAt, x.ContractSentAt);
    }
}

/// <summary>File returned by the attachment download endpoint.</summary>
public sealed record ConnectionAttachmentFile(byte[] Content, string FileName, string ContentType);
