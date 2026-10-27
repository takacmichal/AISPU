using System.ComponentModel.DataAnnotations;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>
/// Payload for <c>POST /api/connection-requests</c>. The request is bound to an existing installation; the customer
/// and supply point are copied from it. The type (small/large source) is derived from the total inverter AC power.
/// </summary>
public sealed record CreateConnectionRequestRequest : IValidatableObject
{
    [Required]
    public Guid? InstallationId { get; init; }

    /// <summary>Total nominal AC power of all inverters in kW; above 10.8 kW it is a large source.</summary>
    [Required, Range(0.01, 100_000)]
    public decimal? TotalInverterAcPowerKw { get; init; }

    [MaxLength(ConnectionRequest.NoteMaxLength)]
    public string? Note { get; init; }

    /// <summary>At least the single-line diagram and the owner's consent.</summary>
    [Required]
    public List<ConnectionRequestAttachmentRequest>? Attachments { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InstallationId == Guid.Empty)
        {
            yield return new ValidationResult("The InstallationId field must not be an empty id.", [nameof(InstallationId)]);
        }

        foreach (var result in ConnectionRequestRequestValidation.ValidateAttachments(validationContext, Attachments))
        {
            yield return result;
        }
    }
}

/// <summary>Payload for <c>PUT /api/connection-requests/{id}</c>: replaces power, note and attachments while Ready.</summary>
public sealed record UpdateConnectionRequestRequest : IValidatableObject
{
    [Required, Range(0.01, 100_000)]
    public decimal? TotalInverterAcPowerKw { get; init; }

    [MaxLength(ConnectionRequest.NoteMaxLength)]
    public string? Note { get; init; }

    [Required]
    public List<ConnectionRequestAttachmentRequest>? Attachments { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ConnectionRequestRequestValidation.ValidateAttachments(validationContext, Attachments))
        {
            yield return result;
        }
    }
}

/// <summary>One file sent with the request. The content is base64-encoded JSON for the MVP.</summary>
public sealed record ConnectionRequestAttachmentRequest
{
    [Required, EnumDataType(typeof(ConnectionAttachmentKind))]
    public ConnectionAttachmentKind? Kind { get; init; }

    [Required, MinLength(1), MaxLength(ConnectionRequest.FileNameMaxLength)]
    public string? FileName { get; init; }

    /// <summary>MIME type, e.g. <c>application/pdf</c>; defaults to <c>application/octet-stream</c>.</summary>
    [MaxLength(ConnectionRequest.ContentTypeMaxLength)]
    public string? ContentType { get; init; }

    /// <summary>Base64 content of the file; at most 5 MB before encoding.</summary>
    [Required, MinLength(1)]
    public string? ContentBase64 { get; init; }
}

internal static class ConnectionRequestRequestValidation
{
    internal static IEnumerable<ValidationResult> ValidateAttachments(
        ValidationContext context,
        List<ConnectionRequestAttachmentRequest>? attachments)
    {
        if (attachments is null)
        {
            yield break;
        }

        if (attachments.Count > ConnectionRequest.MaxAttachments)
        {
            yield return new ValidationResult(
                $"At most {ConnectionRequest.MaxAttachments} attachments can be sent with a connection request.",
                [nameof(CreateConnectionRequestRequest.Attachments)]);
        }

        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];
            if (attachment is null)
            {
                yield return new ValidationResult("Attachment must not be null.", [$"Attachments[{i}]"]);
                continue;
            }

            foreach (var (member, messages) in DataAnnotationsValidator.Validate(attachment, context))
            {
                var prefix = string.IsNullOrEmpty(member) ? $"Attachments[{i}]" : $"Attachments[{i}].{member}";
                foreach (var message in messages)
                {
                    yield return new ValidationResult(message, [prefix]);
                }
            }
        }
    }
}

public sealed record ConnectionRequestAttachmentResponse(
    Guid Id,
    ConnectionAttachmentKind Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    string DownloadUrl)
{
    internal static ConnectionRequestAttachmentResponse FromEntity(Guid requestId, ConnectionRequestAttachment attachment) =>
        new(
            attachment.Id,
            attachment.Kind,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            BuildDownloadUrl(requestId, attachment.Id));

    internal static string BuildDownloadUrl(Guid requestId, Guid attachmentId) =>
        $"/api/connection-requests/{requestId}/attachments/{attachmentId}";
}

public sealed record ConnectionRequestResponse(
    Guid Id,
    Guid InstallationId,
    Guid CustomerId,
    Guid SupplyPointId,
    ConnectionSourceType Type,
    decimal TotalInverterAcPowerKw,
    ConnectionRequestStatus Status,
    string? Note,
    IReadOnlyList<ConnectionRequestAttachmentResponse> Attachments,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    internal static ConnectionRequestResponse FromEntity(ConnectionRequest request) =>
        From(request, request.Attachments.Select(a => ConnectionRequestAttachmentResponse.FromEntity(request.Id, a)).ToList());

    internal static ConnectionRequestResponse From(
        ConnectionRequest request,
        IReadOnlyList<ConnectionRequestAttachmentResponse> attachments) => new(
        request.Id,
        request.InstallationId,
        request.CustomerId,
        request.SupplyPointId,
        request.Type,
        request.TotalInverterAcPowerKw,
        request.Status,
        request.Note,
        attachments,
        request.CreatedBy,
        request.CreatedAt,
        request.UpdatedAt);
}

/// <summary>File returned by the attachment download endpoint.</summary>
public sealed record ConnectionAttachmentFile(byte[] Content, string FileName, string ContentType);
