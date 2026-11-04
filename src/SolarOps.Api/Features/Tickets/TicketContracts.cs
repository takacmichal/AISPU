using System.ComponentModel.DataAnnotations;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Tickets;

/// <summary>A photo or document sent with a new ticket as base64.</summary>
public sealed record TicketAttachmentRequest
{
    [Required, MaxLength(ServiceTicket.FileNameMaxLength)]
    public string? FileName { get; init; }

    /// <summary>One of <c>image/jpeg</c>, <c>image/png</c>, <c>application/pdf</c>.</summary>
    [Required, MaxLength(ServiceTicket.ContentTypeMaxLength)]
    public string? ContentType { get; init; }

    /// <summary>Base64 content; at most <see cref="ServiceTicket.MaxAttachmentBytes"/> before encoding.</summary>
    [Required, MaxLength(ServiceTicket.MaxAttachmentBase64Length)]
    public string? ContentBase64 { get; init; }
}

/// <summary>Payload of <c>POST /api/tickets</c>: a fault report for an installation (state <c>New</c>).</summary>
public sealed record CreateTicketRequest : IValidatableObject
{
    [Required]
    public Guid? InstallationId { get; init; }

    [Required, MaxLength(ServiceTicket.TitleMaxLength)]
    public string? Title { get; init; }

    [Required, MaxLength(ServiceTicket.DescriptionMaxLength)]
    public string? Description { get; init; }

    /// <summary>Initial priority set by the back office; default <c>Medium</c>. A customer report always starts as <c>Medium</c>.</summary>
    [EnumDataType(typeof(ServiceTicketPriority))]
    public ServiceTicketPriority? Priority { get; init; }

    [MaxLength(ServiceTicket.MaxAttachments)]
    public IReadOnlyList<TicketAttachmentRequest>? Attachments { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InstallationId == Guid.Empty)
        {
            yield return new ValidationResult("The InstallationId field must not be an empty id.", [nameof(InstallationId)]);
        }

        if (Attachments is null)
        {
            yield break;
        }

        // The validation filter checks top-level members only, so items are checked here.
        foreach (var (attachment, index) in Attachments.Select((a, i) => (a, i)))
        {
            var prefix = $"{nameof(Attachments)}[{index}]";
            if (attachment is null)
            {
                yield return new ValidationResult("Attachment must not be null.", [prefix]);
                continue;
            }

            foreach (var (member, messages) in DataAnnotationsValidator.Validate(attachment, validationContext))
            {
                foreach (var message in messages)
                {
                    yield return new ValidationResult(message, [$"{prefix}.{member}"]);
                }
            }

            if (!string.IsNullOrWhiteSpace(attachment.ContentType)
                && !ServiceTicket.AllowedContentTypes.Contains(attachment.ContentType.Trim()))
            {
                yield return new ValidationResult(
                    $"The ContentType must be one of: {string.Join(", ", ServiceTicket.AllowedContentTypes)}.",
                    [$"{prefix}.{nameof(TicketAttachmentRequest.ContentType)}"]);
            }
        }
    }
}

/// <summary>Payload of <c>PUT /api/tickets/{id}/triage</c>: decide the priority of a new ticket.</summary>
public sealed record TriageTicketRequest
{
    [Required, EnumDataType(typeof(ServiceTicketPriority))]
    public ServiceTicketPriority? Priority { get; init; }

    [MaxLength(ServiceTicket.NoteMaxLength)]
    public string? Note { get; init; }
}

/// <summary>Payload of <c>POST /api/tickets/{id}/transition</c>.</summary>
public sealed record TransitionTicketRequest
{
    /// <summary>Target state; <c>VisitScheduled</c>, <c>OnSite</c> and <c>Verified</c> are reserved for visits (#36, #37).</summary>
    [Required, EnumDataType(typeof(ServiceTicketState))]
    public ServiceTicketState? State { get; init; }

    /// <summary>Internal note stored in the history; not shown to the customer.</summary>
    [MaxLength(ServiceTicket.NoteMaxLength)]
    public string? Note { get; init; }
}

/// <summary>Payload of <c>POST /api/tickets/{id}/comments</c>.</summary>
public sealed record AddTicketCommentRequest
{
    [Required, MaxLength(ServiceTicket.CommentMaxLength)]
    public string? Body { get; init; }

    /// <summary>True when the customer may read the comment; default false (internal note).</summary>
    public bool IsPublic { get; init; }
}

/// <summary>Attachment metadata; the content is downloaded from <see cref="DownloadUrl"/>.</summary>
public sealed record TicketAttachmentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string DownloadUrl);

/// <summary>Ticket detail returned by the API.</summary>
public sealed record TicketResponse(
    Guid Id,
    Guid InstallationId,
    Guid CustomerId,
    string Title,
    string Description,
    ServiceTicketPriority Priority,
    ServiceTicketOrigin Origin,
    ServiceTicketState State,
    IReadOnlyList<ServiceTicketState> AllowedTransitions,
    IReadOnlyList<TicketAttachmentResponse> Attachments,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    internal static TicketResponse From(ServiceTicket t, IReadOnlyList<TicketAttachmentResponse> attachments) => new(
        t.Id, t.InstallationId, t.CustomerId, t.Title, t.Description, t.Priority, t.Origin, t.State,
        t.AllowedTransitions, attachments, t.CreatedBy, t.CreatedAt, t.UpdatedAt);

    internal static TicketResponse From(ServiceTicket t) => From(
        t,
        t.Attachments
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => TicketAttachments.ToResponse(t.Id, a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt))
            .ToList());
}

/// <summary>One row of the ticket list.</summary>
public sealed record TicketListItem(
    Guid Id,
    Guid InstallationId,
    Guid CustomerId,
    string Title,
    ServiceTicketPriority Priority,
    ServiceTicketOrigin Origin,
    ServiceTicketState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Page of tickets returned by <c>GET /api/tickets</c>.</summary>
public sealed record TicketListResponse(
    IReadOnlyList<TicketListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>History entry. <see cref="Note"/> is null for customers (internal note).</summary>
public sealed record TicketStateChangeResponse(
    Guid Id,
    ServiceTicketState? FromState,
    ServiceTicketState ToState,
    Guid ChangedBy,
    DateTimeOffset ChangedAt,
    string? Note);

public sealed record TicketCommentResponse(
    Guid Id,
    Guid AuthorId,
    string Body,
    bool IsPublic,
    DateTimeOffset CreatedAt)
{
    internal static TicketCommentResponse From(ServiceTicketComment c) => new(c.Id, c.AuthorId, c.Body, c.IsPublic, c.CreatedAt);
}

/// <summary>File returned by the attachment download endpoint.</summary>
public sealed record TicketAttachmentFile(byte[] Content, string FileName, string ContentType);

internal static class TicketAttachments
{
    internal static TicketAttachmentResponse ToResponse(
        Guid ticketId, Guid id, string fileName, string contentType, long sizeBytes, DateTimeOffset createdAt) =>
        new(id, fileName, contentType, sizeBytes, createdAt, $"/api/tickets/{ticketId}/attachments/{id}");
}
