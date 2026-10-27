using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>Lifecycle of a connection request (ADR #217, BPMN #64, mapping #69). Only <c>Draft</c> is produced by #85; the rest follows with #86 and #87.</summary>
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

    /// <summary>Edits a request that has not been submitted yet; replaces the attachment set as a whole.</summary>
    public void Update(
        string eicCode,
        decimal ratedAcPowerKw,
        decimal smallSourceMaxKw,
        int? mainBreakerAmps,
        IReadOnlyCollection<AttachmentInput> attachments,
        DateTimeOffset now)
    {
        if (State != ConnectionRequestState.Draft)
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
