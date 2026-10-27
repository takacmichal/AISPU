using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>Branch of the connection process, derived from the total nominal AC power of the inverters (spike #217).</summary>
public enum ConnectionSourceType
{
    /// <summary>Small source up to the configured threshold (10.8 kW by default).</summary>
    SmallSource,

    /// <summary>Larger source above the threshold; the distributor also assesses capacity/feasibility.</summary>
    LargeSource,
}

/// <summary>
/// Lifecycle of a connection request (BPMN #64, mapping #69). A request starts <c>Ready</c> as soon as it is
/// created with all mandatory attachments. Submitting and distributor decisions follow in #86/#87.
/// </summary>
public enum ConnectionRequestStatus
{
    /// <summary>Pripravená: complete draft that can be submitted to the distributor.</summary>
    Ready,
}

/// <summary>Kinds of attachment required or commonly sent with a connection request (spike #217).</summary>
public enum ConnectionAttachmentKind
{
    /// <summary>Single-line diagram (jednopólová schéma); mandatory.</summary>
    SingleLineDiagram,

    /// <summary>Owner's consent (súhlas vlastníka); mandatory.</summary>
    OwnerConsent,

    InverterDatasheet,
    PanelDatasheet,
    SwitchboardPhoto,
    PowerOfAttorney,
    Other,
}

/// <summary>Attachment content decoded from the request DTO and ready to be stored with the request.</summary>
public sealed record ConnectionAttachmentData(
    ConnectionAttachmentKind Kind,
    string FileName,
    string ContentType,
    byte[] Content);

/// <summary>
/// Request to connect a PV plant to the distribution grid (#85). It is bound to an installation and copies the
/// customer and supply point from it. The type follows from the total inverter AC power; attachments are stored
/// with the request so it can be handed to the distributor. Other modules are referenced by id only.
/// </summary>
public sealed class ConnectionRequest
{
    /// <summary>ZSD threshold for a small source (spike #217); configurable through <see cref="ConnectionRequestOptions"/>.</summary>
    public const decimal DefaultSmallSourceMaxKw = 10.8m;

    public const int NoteMaxLength = 2000;
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 100;
    public const int MaxAttachments = 10;

    /// <summary>Upper limit for one attachment; the file is stored in the database for the MVP.</summary>
    public const long MaxAttachmentBytes = 5 * 1024 * 1024;

    private readonly List<ConnectionRequestAttachment> _attachments = [];

    private ConnectionRequest()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The installation the request is bound to; one connection request per installation.</summary>
    public Guid InstallationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid SupplyPointId { get; private set; }

    public ConnectionSourceType Type { get; private set; }

    /// <summary>Total nominal AC power of all inverters in kW; decides <see cref="Type"/>.</summary>
    public decimal TotalInverterAcPowerKw { get; private set; }

    public ConnectionRequestStatus Status { get; private set; }

    public string? Note { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ConnectionRequestAttachment> Attachments => _attachments;

    /// <summary>
    /// Creates the request in state <see cref="ConnectionRequestStatus.Ready"/>. The single-line diagram and the
    /// owner's consent are mandatory, so an incomplete request can never leave the salesperson's desk.
    /// </summary>
    public static ConnectionRequest Create(
        Guid installationId,
        Guid customerId,
        Guid supplyPointId,
        decimal totalInverterAcPowerKw,
        string? note,
        IReadOnlyList<ConnectionAttachmentData> attachments,
        decimal smallSourceMaxKw,
        Guid createdBy,
        DateTimeOffset now)
    {
        EnsureAttachments(attachments);

        var request = new ConnectionRequest
        {
            Id = Guid.CreateVersion7(),
            InstallationId = installationId,
            CustomerId = customerId,
            SupplyPointId = supplyPointId,
            Type = Classify(totalInverterAcPowerKw, smallSourceMaxKw),
            TotalInverterAcPowerKw = totalInverterAcPowerKw,
            Status = ConnectionRequestStatus.Ready,
            Note = Normalize(note),
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
        request.AddAttachments(attachments);
        return request;
    }

    /// <summary>Replaces the power, note and attachment set while the request is still <c>Ready</c>.</summary>
    public void Update(
        decimal totalInverterAcPowerKw,
        string? note,
        IReadOnlyList<ConnectionAttachmentData> attachments,
        decimal smallSourceMaxKw,
        DateTimeOffset now)
    {
        if (Status != ConnectionRequestStatus.Ready)
        {
            throw new DomainException($"A connection request in state {Status} can no longer be edited.");
        }

        EnsureAttachments(attachments);

        Type = Classify(totalInverterAcPowerKw, smallSourceMaxKw);
        TotalInverterAcPowerKw = totalInverterAcPowerKw;
        Note = Normalize(note);
        UpdatedAt = now;

        _attachments.Clear();
        AddAttachments(attachments);
    }

    /// <summary>Small source when the total inverter AC power is at most <paramref name="smallSourceMaxKw"/>.</summary>
    public static ConnectionSourceType Classify(decimal totalInverterAcPowerKw, decimal smallSourceMaxKw)
    {
        if (totalInverterAcPowerKw <= 0)
        {
            throw new RequestValidationException(
                nameof(TotalInverterAcPowerKw),
                "The total inverter AC power must be greater than 0.");
        }

        if (smallSourceMaxKw <= 0)
        {
            throw new InvalidOperationException("The small-source threshold must be greater than 0.");
        }

        return totalInverterAcPowerKw <= smallSourceMaxKw ? ConnectionSourceType.SmallSource : ConnectionSourceType.LargeSource;
    }

    private void AddAttachments(IReadOnlyList<ConnectionAttachmentData> attachments)
    {
        foreach (var attachment in attachments)
        {
            _attachments.Add(new ConnectionRequestAttachment(Id, attachment));
        }
    }

    private static void EnsureAttachments(IReadOnlyList<ConnectionAttachmentData> attachments)
    {
        var errors = new Dictionary<string, string[]>();
        var attachmentErrors = new List<string>();
        if (attachments.Count > MaxAttachments)
        {
            attachmentErrors.Add($"At most {MaxAttachments} attachments can be sent with a connection request.");
        }

        var missing = new List<string>();
        if (!attachments.Any(a => a.Kind == ConnectionAttachmentKind.SingleLineDiagram))
        {
            missing.Add("single-line diagram (SingleLineDiagram)");
        }

        if (!attachments.Any(a => a.Kind == ConnectionAttachmentKind.OwnerConsent))
        {
            missing.Add("owner's consent (OwnerConsent)");
        }

        if (missing.Count > 0)
        {
            attachmentErrors.Add($"The connection request is missing the mandatory attachment(s): {string.Join(", ", missing)}.");
        }

        if (attachmentErrors.Count > 0)
        {
            errors["Attachments"] = [.. attachmentErrors];
            throw new RequestValidationException(errors);
        }

        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];
            if (string.IsNullOrWhiteSpace(attachment.FileName))
            {
                errors[$"Attachments[{i}].FileName"] = ["A file name is required."];
            }

            if (attachment.Content.Length == 0)
            {
                errors[$"Attachments[{i}].ContentBase64"] = ["The attachment is empty."];
            }
            else if (attachment.Content.LongLength > MaxAttachmentBytes)
            {
                errors[$"Attachments[{i}].ContentBase64"] =
                    [$"An attachment can be at most {MaxAttachmentBytes / (1024 * 1024)} MB."];
            }
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One file attached to a connection request; the content is stored in the database for the MVP.</summary>
public sealed class ConnectionRequestAttachment
{
    private ConnectionRequestAttachment()
    {
        FileName = ContentType = string.Empty;
        Content = [];
    }

    internal ConnectionRequestAttachment(Guid connectionRequestId, ConnectionAttachmentData data)
    {
        Id = Guid.CreateVersion7();
        ConnectionRequestId = connectionRequestId;
        Kind = data.Kind;
        FileName = data.FileName.Trim();
        ContentType = string.IsNullOrWhiteSpace(data.ContentType) ? "application/octet-stream" : data.ContentType.Trim();
        Content = data.Content;
        SizeBytes = data.Content.LongLength;
    }

    public Guid Id { get; private set; }

    public Guid ConnectionRequestId { get; private set; }

    public ConnectionAttachmentKind Kind { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>Raw file bytes; never returned in the request detail (use the download endpoint).</summary>
    public byte[] Content { get; private set; }
}

/// <summary>Configuration of the connection request process (spike #217); defaults are in appsettings.</summary>
public sealed class ConnectionRequestOptions
{
    public const string SectionName = "ConnectionRequest";

    /// <summary>Total inverter AC power up to this value is a small source (ZSD: 10.8 kW).</summary>
    public decimal SmallSourceMaxKw { get; set; } = ConnectionRequest.DefaultSmallSourceMaxKw;
}
