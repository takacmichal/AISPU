using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>
/// Lifecycle of a connection request (ADR #217, BPMN #64, mapping #69). #85 produces <c>Draft</c>; #86 adds
/// <c>Submitted</c>, <c>InfoRequested</c>, <c>Rejected</c>, <c>ContractOffered</c> ("schválená"), <c>ContractSigned</c> and
/// <c>ContractSubmitted</c>; <c>Concluded</c> is the distributor's final act (simulator #87).
/// </summary>
public enum ConnectionRequestState
{
    Draft,
    Submitted,
    InfoRequested,
    Rejected,
    ContractOffered,
    ContractSigned,
    ContractSubmitted,
    Concluded,
}

/// <summary>Branch of the process derived from the rated AC power (ADR #217).</summary>
public enum ConnectionRequestType
{
    SmallSource,
    LargeSource,
}

/// <summary>Kind of a document attached to a connection request (ADR #217, list of attachments).</summary>
public enum ConnectionRequestAttachmentKind
{
    /// <summary>Single-line diagram (jednopólová schéma). Required.</summary>
    SingleLineDiagram,

    /// <summary>Consent of the owner of the supply point (súhlas vlastníka). Required.</summary>
    OwnerConsent,

    Datasheet,
    SwitchboardPhoto,
    PowerOfAttorney,
    CadastreExtract,
}

/// <summary>
/// Metadata of an attachment as given by the client. <see cref="Content"/> is optional: older clients may send
/// only metadata, while the UI sends the file as base64 so it can be downloaded later.
/// </summary>
public sealed record AttachmentInput(
    ConnectionRequestAttachmentKind Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    byte[]? Content = null);

/// <summary>
/// Request to connect a PV plant of an installation to the distribution grid. Bound to the installation, customer and
/// supply point by id only. A request is created in <see cref="ConnectionRequestState.Draft"/> ("Pripravená").
/// </summary>
public sealed class ConnectionRequest
{
    public const int EicMaxLength = 32;
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 127;
    public const int DecisionNoteMaxLength = 1000;

    /// <summary>
    /// Attachments per request. Together with <see cref="MaxAttachmentBytes"/> it bounds the request body
    /// (see <see cref="MaxRequestBodyBytes"/>).
    /// </summary>
    public const int MaxAttachments = 10;

    /// <summary>Largest file stored with the request; the MVP keeps the bytes in the database.</summary>
    public const long MaxAttachmentBytes = 5 * 1024 * 1024;

    /// <summary>Upper bound of the base64 payload of <see cref="MaxAttachmentBytes"/> (4 characters per 3 bytes).</summary>
    public const int MaxAttachmentBase64Length = (int)(((MaxAttachmentBytes + 2) / 3) * 4);

    /// <summary>
    /// Body limit for create/update: every attachment at its base64 maximum plus room for the other fields.
    /// The endpoints declare it with <c>[RequestSizeLimit]</c>, because the Kestrel default (~30 MB) would
    /// reject requests that pass the per-attachment limits.
    /// </summary>
    public const long MaxRequestBodyBytes = (MaxAttachments * (long)MaxAttachmentBase64Length) + (64 * 1024);

    /// <summary>Attachments every request must have (story #85).</summary>
    public static readonly IReadOnlyList<ConnectionRequestAttachmentKind> RequiredAttachmentKinds =
        [ConnectionRequestAttachmentKind.SingleLineDiagram, ConnectionRequestAttachmentKind.OwnerConsent];

    private readonly List<ConnectionRequestAttachment> _attachments = [];

    private ConnectionRequest()
    {
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid SupplyPointId { get; private set; }

    /// <summary>EIC code of the supply point (odberné miesto).</summary>
    public string EicCode { get; private set; } = string.Empty;

    /// <summary>Total rated AC power of the inverters in kW; decides the <see cref="Type"/>.</summary>
    public decimal RatedAcPowerKw { get; private set; }

    /// <summary>Main breaker of the supply point in amperes per phase (optional).</summary>
    public int? MainBreakerAmps { get; private set; }

    public ConnectionRequestType Type { get; private set; }

    public ConnectionRequestState State { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Moment of the last (re)submission to the distributor; starts the proposal deadline.</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>Last day on which the distributor should answer; fixed at submission from the configured deadlines.</summary>
    public DateOnly? ProposalDueDate { get; private set; }

    /// <summary>Moment of the distributor's last decision (info request, rejection, contract offer).</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Reason of the rejection or what must be completed; optional for a contract offer.</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>Moment the technical conditions of the offered contract were checked (BPMN #64); required before the contract is sent.</summary>
    public DateTimeOffset? ConditionsReviewedAt { get; private set; }

    /// <summary>Moment the contract was sent to the customer; required before the customer's signature is recorded.</summary>
    public DateTimeOffset? ContractSentAt { get; private set; }

    public IReadOnlyList<ConnectionRequestAttachment> Attachments => _attachments;

    // smallSourceMaxKw: upper bound of a small source in kW (10.8 by ADR #217, configurable).
    public static ConnectionRequest Create(
        Guid installationId,
        Guid customerId,
        Guid supplyPointId,
        string eicCode,
        decimal ratedAcPowerKw,
        decimal smallSourceMaxKw,
        int? mainBreakerAmps,
        IReadOnlyCollection<AttachmentInput> attachments,
        Guid createdBy,
        DateTimeOffset now)
    {
        EnsureRequiredAttachments(attachments);

        var request = new ConnectionRequest
        {
            Id = Guid.CreateVersion7(),
            InstallationId = installationId,
            CustomerId = customerId,
            SupplyPointId = supplyPointId,
            EicCode = eicCode.Trim(),
            RatedAcPowerKw = ratedAcPowerKw,
            MainBreakerAmps = mainBreakerAmps,
            Type = Classify(ratedAcPowerKw, smallSourceMaxKw),
            State = ConnectionRequestState.Draft,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
        request.ReplaceAttachments(attachments, now);
        return request;
    }

    /// <summary>Submits a prepared request to the distributor (Draft to Submitted); <paramref name="proposalDueDate"/> starts the deadline.</summary>
    public void Submit(DateOnly proposalDueDate, DateTimeOffset now)
    {
        EnsureState(ConnectionRequestState.Draft, "submitted");
        MarkSubmitted(proposalDueDate, now);
    }

    /// <summary>Sends the corrected request again (InfoRequested to Submitted); the deadline starts anew.</summary>
    public void Resubmit(DateOnly proposalDueDate, DateTimeOffset now)
    {
        EnsureState(ConnectionRequestState.InfoRequested, "resubmitted");
        MarkSubmitted(proposalDueDate, now);
        DecidedAt = null;
        DecisionNote = null;
    }

    /// <summary>Decision of the distributor: the request must be completed (Submitted to InfoRequested).</summary>
    public void RequestInfo(string note, DateTimeOffset now) => Decide(ConnectionRequestState.InfoRequested, RequireNote(note), now);

    /// <summary>Decision of the distributor: the request is refused (Submitted to Rejected).</summary>
    public void Reject(string reason, DateTimeOffset now) => Decide(ConnectionRequestState.Rejected, RequireNote(reason), now);

    /// <summary>Decision of the distributor: approved, a contract is offered (Submitted to ContractOffered).</summary>
    public void OfferContract(string? note, DateTimeOffset now) =>
        Decide(ConnectionRequestState.ContractOffered, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), now);

    /// <summary>Checks the technical conditions of the offered contract (stays in ContractOffered).</summary>
    public void ReviewConditions(DateTimeOffset now)
    {
        EnsureState(ConnectionRequestState.ContractOffered, "reviewed");
        ConditionsReviewedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Sends the offered contract to the customer (stays in ContractOffered); the conditions must be reviewed first.</summary>
    public void SendContract(DateTimeOffset now)
    {
        EnsureState(ConnectionRequestState.ContractOffered, "sent to the customer");
        if (ConditionsReviewedAt is null)
        {
            throw new DomainException("The technical conditions must be reviewed before the contract is sent to the customer.");
        }

        ContractSentAt = now;
        UpdatedAt = now;
    }

    /// <summary>Records the customer's signature (ContractOffered to ContractSigned); the contract must have been sent.</summary>
    public void CustomerSigned(DateTimeOffset now)
    {
        EnsureState(ConnectionRequestState.ContractOffered, "marked as signed");
        if (ContractSentAt is null)
        {
            throw new DomainException("The contract must be sent to the customer before the signature is recorded.");
        }

        State = ConnectionRequestState.ContractSigned;
        UpdatedAt = now;
    }

    /// <summary>Sends the signed contract to the distributor (ContractSigned to ContractSubmitted).</summary>
    public void SubmitSignedContract(DateTimeOffset now)
    {
        EnsureState(ConnectionRequestState.ContractSigned, "submitted with the signed contract");
        State = ConnectionRequestState.ContractSubmitted;
        UpdatedAt = now;
    }

    private void MarkSubmitted(DateOnly proposalDueDate, DateTimeOffset now)
    {
        State = ConnectionRequestState.Submitted;
        SubmittedAt = now;
        ProposalDueDate = proposalDueDate;
        UpdatedAt = now;
    }

    private void Decide(ConnectionRequestState next, string? note, DateTimeOffset now)
    {
        if (State != ConnectionRequestState.Submitted)
        {
            throw new DomainException($"A connection request in state {State} cannot be moved to {next}; only a submitted request awaits a decision.");
        }

        State = next;
        DecisionNote = note;
        DecidedAt = now;
        UpdatedAt = now;
    }

    private void EnsureState(ConnectionRequestState expected, string action)
    {
        if (State != expected)
        {
            throw new DomainException($"A connection request in state {State} cannot be {action}; it must be in state {expected}.");
        }
    }

    private static string RequireNote(string note) =>
        string.IsNullOrWhiteSpace(note) ? throw new DomainException("A note is required.") : note.Trim();

    /// <summary>Edits a request before it is submitted or while the distributor waits for completion; replaces the attachment set as a whole.</summary>
    public void Update(
        string eicCode,
        decimal ratedAcPowerKw,
        decimal smallSourceMaxKw,
        int? mainBreakerAmps,
        IReadOnlyCollection<AttachmentInput> attachments,
        DateTimeOffset now)
    {
        if (State is not (ConnectionRequestState.Draft or ConnectionRequestState.InfoRequested))
        {
            throw new DomainException($"A connection request in state {State} can no longer be edited.");
        }

        EnsureRequiredAttachments(attachments);

        EicCode = eicCode.Trim();
        RatedAcPowerKw = ratedAcPowerKw;
        MainBreakerAmps = mainBreakerAmps;
        Type = Classify(ratedAcPowerKw, smallSourceMaxKw);
        UpdatedAt = now;
        ReplaceAttachments(attachments, now);
    }

    private static ConnectionRequestType Classify(decimal ratedAcPowerKw, decimal smallSourceMaxKw) =>
        ratedAcPowerKw <= smallSourceMaxKw ? ConnectionRequestType.SmallSource : ConnectionRequestType.LargeSource;

    private static void EnsureRequiredAttachments(IReadOnlyCollection<AttachmentInput> attachments)
    {
        var missing = RequiredAttachmentKinds.Where(k => attachments.All(a => a.Kind != k)).ToList();
        if (missing.Count > 0)
        {
            throw new DomainException($"Missing required attachments: {string.Join(", ", missing)}.");
        }
    }

    private void ReplaceAttachments(IReadOnlyCollection<AttachmentInput> attachments, DateTimeOffset now)
    {
        _attachments.Clear();
        _attachments.AddRange(attachments.Select(a => ConnectionRequestAttachment.Create(Id, a, now)));
    }
}

/// <summary>One document attached to a connection request; the content is optional for metadata-only clients.</summary>
public sealed class ConnectionRequestAttachment
{
    private ConnectionRequestAttachment()
    {
    }

    public Guid Id { get; private set; }

    public Guid ConnectionRequestId { get; private set; }

    public ConnectionRequestAttachmentKind Kind { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>Raw file bytes; null when the client sent metadata only. Never returned in the request detail.</summary>
    public byte[]? Content { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True when the raw bytes are stored and the download endpoint can return them.</summary>
    public bool HasContent { get; private set; }

    internal static ConnectionRequestAttachment Create(Guid requestId, AttachmentInput input, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ConnectionRequestId = requestId,
        Kind = input.Kind,
        FileName = input.FileName.Trim(),
        ContentType = input.ContentType.Trim(),
        SizeBytes = input.Content?.LongLength ?? input.SizeBytes,
        Content = input.Content,
        HasContent = input.Content is { Length: > 0 },
        CreatedAt = now,
    };
}
