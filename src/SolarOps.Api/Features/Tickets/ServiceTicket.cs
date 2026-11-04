using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Tickets;

/// <summary>
/// Lifecycle of a service ticket (mapping #69, BPMN #67 and #68). Story #121 names five states (Nový, Pridelený,
/// V riešení, Vyriešený, Uzavretý); the mapping document is the source of truth and splits them along the BPMN tasks.
/// </summary>
public enum ServiceTicketState
{
    /// <summary>Report received, ticket created (Nový).</summary>
    New,

    /// <summary>Type and priority decided by the back office.</summary>
    Triaged,

    /// <summary>The fault is being diagnosed remotely (V riešení).</summary>
    RemoteDiagnosis,

    /// <summary>A technician visit is planned (#36).</summary>
    VisitScheduled,

    /// <summary>The technician is on site (#37).</summary>
    OnSite,

    /// <summary>The technician verified that production is restored (#37).</summary>
    Verified,

    /// <summary>The fault is fixed (Vyriešený).</summary>
    Resolved,

    /// <summary>The ticket is closed and the customer informed (Uzavretý).</summary>
    Closed,
}

/// <summary>Priority of a ticket; the levels match the severity of an under-production event (ADR #194).</summary>
public enum ServiceTicketPriority
{
    Low,
    Medium,
    High,
}

/// <summary>Where a ticket came from (mapping #69). Story #122: the origin tells manual and automatic tickets apart.</summary>
public enum ServiceTicketOrigin
{
    /// <summary>Reported by the customer through the portal.</summary>
    CustomerReport,

    /// <summary>Created by the back office or a technician, e.g. after a phone call.</summary>
    Manual,

    /// <summary>Created from a confirmed ML alert (#115); not produced by this module yet.</summary>
    AnomalyDetected,
}

/// <summary>A file attached when the ticket is created (photo of the fault).</summary>
public sealed record TicketAttachmentInput(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Service ticket for an installation. Bound to the installation and its customer by id only (modules share no foreign
/// keys). Every state change appends an immutable <see cref="ServiceTicketStateChange"/>.
/// </summary>
/// <remarks>
/// Extension point: technician assignment and visit scheduling (#36) and the intervention record with materials and
/// closing confirmation (#37) drive the states in <see cref="VisitWorkflowStates"/>. They call
/// <see cref="TransitionTo"/> from their own endpoints and keep their data (technician, visit, protocol) in their own
/// entities referencing <see cref="Id"/>; the generic transition endpoint of this module refuses those states.
/// </remarks>
public sealed class ServiceTicket
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int NoteMaxLength = 2000;
    public const int CommentMaxLength = 4000;
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 127;

    /// <summary>Photos or documents per ticket.</summary>
    public const int MaxAttachments = 5;

    /// <summary>Largest file stored with a ticket; the MVP keeps the bytes in the database like connection requests do.</summary>
    public const long MaxAttachmentBytes = 5 * 1024 * 1024;

    /// <summary>Upper bound of the base64 payload of <see cref="MaxAttachmentBytes"/> (4 characters per 3 bytes).</summary>
    public const int MaxAttachmentBase64Length = (int)(((MaxAttachmentBytes + 2) / 3) * 4);

    /// <summary>Body limit for creating a ticket: every attachment at its base64 maximum plus room for the text.</summary>
    public const long MaxRequestBodyBytes = (MaxAttachments * (long)MaxAttachmentBase64Length) + (64 * 1024);

    /// <summary>Media types a customer may attach (story #120: the attachment is limited by size and type).</summary>
    public static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(["image/jpeg", "image/png", "application/pdf"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// States driven by technician visits (#36) and the intervention record (#37). The generic transition endpoint
    /// refuses them, so the visit data cannot be skipped.
    /// </summary>
    public static readonly IReadOnlySet<ServiceTicketState> VisitWorkflowStates =
        new HashSet<ServiceTicketState>([ServiceTicketState.VisitScheduled, ServiceTicketState.OnSite, ServiceTicketState.Verified]);

    private static readonly IReadOnlyDictionary<ServiceTicketState, ServiceTicketState[]> AllowedTransitionMap =
        new Dictionary<ServiceTicketState, ServiceTicketState[]>
        {
            [ServiceTicketState.New] = [ServiceTicketState.Triaged],
            [ServiceTicketState.Triaged] = [ServiceTicketState.RemoteDiagnosis],

            // Gateway "Vyriešené na diaľku?" of BPMN #67: yes resolves, no schedules a visit.
            [ServiceTicketState.RemoteDiagnosis] = [ServiceTicketState.Resolved, ServiceTicketState.VisitScheduled],
            [ServiceTicketState.VisitScheduled] = [ServiceTicketState.OnSite],
            [ServiceTicketState.OnSite] = [ServiceTicketState.Verified],
            [ServiceTicketState.Verified] = [ServiceTicketState.Resolved],
            [ServiceTicketState.Resolved] = [ServiceTicketState.Closed],
            [ServiceTicketState.Closed] = [],
        };

    private readonly List<ServiceTicketStateChange> _stateChanges = [];
    private readonly List<ServiceTicketAttachment> _attachments = [];

    private ServiceTicket()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Installation of the Installations module, referenced by id only.</summary>
    public Guid InstallationId { get; private set; }

    /// <summary>Owner of the installation, copied at creation so the customer scope can filter without a join.</summary>
    public Guid CustomerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public ServiceTicketPriority Priority { get; private set; }

    public ServiceTicketOrigin Origin { get; private set; }

    public ServiceTicketState State { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ServiceTicketStateChange> StateChanges => _stateChanges;

    public IReadOnlyList<ServiceTicketAttachment> Attachments => _attachments;

    public IReadOnlyList<ServiceTicketState> AllowedTransitions => AllowedTransitionMap[State];

    /// <summary>Creates a ticket in state <see cref="ServiceTicketState.New"/> with its creation history entry.</summary>
    public static ServiceTicket Create(
        Guid installationId,
        Guid customerId,
        string title,
        string description,
        ServiceTicketPriority priority,
        ServiceTicketOrigin origin,
        IReadOnlyCollection<TicketAttachmentInput> attachments,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (installationId == Guid.Empty)
        {
            throw new RequestValidationException("InstallationId", "The InstallationId field must not be an empty id.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new RequestValidationException("Title", "The Title field is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new RequestValidationException("Description", "The Description field is required.");
        }

        var ticket = new ServiceTicket
        {
            Id = Guid.CreateVersion7(),
            InstallationId = installationId,
            CustomerId = customerId,
            Title = title.Trim(),
            Description = description.Trim(),
            Priority = priority,
            Origin = origin,
            State = ServiceTicketState.New,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };

        ticket._attachments.AddRange(attachments.Select(a => ServiceTicketAttachment.Create(ticket.Id, a, now)));
        ticket._stateChanges.Add(ServiceTicketStateChange.Create(ticket.Id, null, ServiceTicketState.New, createdBy, null, now));
        return ticket;
    }

    /// <summary>Decides the priority of a new ticket and moves it to <see cref="ServiceTicketState.Triaged"/>.</summary>
    public void Triage(ServiceTicketPriority priority, Guid by, string? note, DateTimeOffset now)
    {
        TransitionTo(ServiceTicketState.Triaged, by, note, now);
        Priority = priority;
    }

    /// <summary>
    /// Moves the ticket along the automaton and records who did it and when. An edge outside the map throws
    /// <see cref="DomainException"/> (409) and changes nothing.
    /// </summary>
    public void TransitionTo(ServiceTicketState target, Guid by, string? note, DateTimeOffset now)
    {
        if (note?.Length > NoteMaxLength)
        {
            throw new RequestValidationException("Note", $"Note is at most {NoteMaxLength} characters.");
        }

        if (!AllowedTransitionMap[State].Contains(target))
        {
            throw new DomainException($"Service ticket cannot move from {State} to {target}.");
        }

        var from = State;
        State = target;
        UpdatedAt = now;
        _stateChanges.Add(ServiceTicketStateChange.Create(Id, from, target, by, note, now));
    }
}

/// <summary>Immutable history entry: who moved the ticket from which state to which and when. No update or delete exists.</summary>
public sealed class ServiceTicketStateChange
{
    private ServiceTicketStateChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid ServiceTicketId { get; private set; }

    /// <summary>Null for the creation entry.</summary>
    public ServiceTicketState? FromState { get; private set; }

    public ServiceTicketState ToState { get; private set; }

    public Guid ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Internal note of the back office; not shown to customers.</summary>
    public string? Note { get; private set; }

    internal static ServiceTicketStateChange Create(
        Guid ticketId, ServiceTicketState? from, ServiceTicketState to, Guid changedBy, string? note, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        ServiceTicketId = ticketId,
        FromState = from,
        ToState = to,
        ChangedBy = changedBy,
        ChangedAt = at,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
    };
}

/// <summary>
/// Note on a ticket (story #123). Written once, never edited or deleted, so the communication stays traceable.
/// Only public comments are visible to the customer.
/// </summary>
public sealed class ServiceTicketComment
{
    private ServiceTicketComment()
    {
    }

    public Guid Id { get; private set; }

    public Guid ServiceTicketId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    /// <summary>True when the customer may read the comment; internal notes are false.</summary>
    public bool IsPublic { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ServiceTicketComment Create(Guid ticketId, Guid authorId, string body, bool isPublic, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new RequestValidationException("Body", "The Body field is required.");
        }

        return new ServiceTicketComment
        {
            Id = Guid.CreateVersion7(),
            ServiceTicketId = ticketId,
            AuthorId = authorId,
            Body = body.Trim(),
            IsPublic = isPublic,
            CreatedAt = now,
        };
    }
}

/// <summary>File attached to a ticket at creation; the bytes are stored in the database.</summary>
public sealed class ServiceTicketAttachment
{
    private ServiceTicketAttachment()
    {
    }

    public Guid Id { get; private set; }

    public Guid ServiceTicketId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>Raw file bytes; never returned in the ticket detail, only by the download endpoint.</summary>
    public byte[] Content { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    internal static ServiceTicketAttachment Create(Guid ticketId, TicketAttachmentInput input, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ServiceTicketId = ticketId,
        FileName = input.FileName.Trim(),
        ContentType = input.ContentType.Trim().ToLowerInvariant(),
        SizeBytes = input.Content.LongLength,
        Content = input.Content,
        CreatedAt = now,
    };
}
