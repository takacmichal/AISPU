using System.ComponentModel.DataAnnotations;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>Metadata of one attachment. The file content is not uploaded yet (storage is an open question).</summary>
public sealed record AttachmentRequest
{
    [Required]
    public ConnectionRequestAttachmentKind? Kind { get; init; }

    [Required, MaxLength(ConnectionRequest.FileNameMaxLength)]
    public string? FileName { get; init; }

    [Required, MaxLength(ConnectionRequest.ContentTypeMaxLength)]
    public string? ContentType { get; init; }

    [Range(1, long.MaxValue)]
    public long SizeBytes { get; init; }
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

        if (EicCode is not null && string.IsNullOrWhiteSpace(EicCode))
        {
            yield return new ValidationResult("The EicCode field is required.", [nameof(EicCode)]);
        }

        if (Attachments is null)
        {
            yield break;
        }

        // The validation filter checks top-level members only, so items are checked here.
        foreach (var (attachment, index) in Attachments.Select((a, i) => (a, i)))
        {
            var errors = DataAnnotationsValidator.Validate(attachment);
            foreach (var (member, messages) in errors)
            {
                foreach (var message in messages)
                {
                    yield return new ValidationResult(message, [$"{nameof(Attachments)}[{index}].{member}"]);
                }
            }
        }

        var missing = ConnectionRequest.RequiredAttachmentKinds.Where(k => Attachments.All(a => a.Kind != k)).ToList();
        if (missing.Count > 0)
        {
            yield return new ValidationResult($"Missing required attachments: {string.Join(", ", missing)}.", [nameof(Attachments)]);
        }
    }
}

/// <summary>Attachment metadata as returned by the API.</summary>
public sealed record AttachmentResponse(
    Guid Id,
    ConnectionRequestAttachmentKind Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt);

/// <summary>Connection request with its attachments.</summary>
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
    DateTimeOffset UpdatedAt)
{
    internal static ConnectionRequestResponse From(ConnectionRequest x) => new(
        x.Id, x.InstallationId, x.CustomerId, x.SupplyPointId, x.EicCode, x.RatedAcPowerKw, x.MainBreakerAmps, x.Type, x.State,
        x.Attachments
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new AttachmentResponse(a.Id, a.Kind, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt))
            .ToList(),
        x.CreatedAt, x.UpdatedAt);
}
