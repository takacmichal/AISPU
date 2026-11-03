using System.Security.Cryptography;

namespace SolarOps.Api.Features.Installations;

/// <summary>Outcome of the revision (odborná prehliadka a skúška) of a PV plant (#91).</summary>
public enum RevisionResult
{
    Passed,
    Failed,
}

/// <summary>
/// Revision report (revízna správa) of an installation (#91). The revision itself is done by an external revision
/// technician; the system records its result, validity and the signed report file. A failed report can be followed by
/// a new one after repairs, so reports are kept as a history. The file bytes are only returned by the download endpoint.
/// </summary>
public sealed class RevisionReport
{
    public const int InspectorNameMaxLength = 200;
    public const int CertificateMaxLength = 50;
    public const int FindingsMaxLength = 2000;
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 127;

    /// <summary>Largest stored file; the MVP keeps the bytes in the database like connection request attachments.</summary>
    public const int MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>Upper bound of the base64 payload of <see cref="MaxFileBytes"/>.</summary>
    public const int MaxFileBase64Length = ((MaxFileBytes + 2) / 3) * 4;

    /// <summary>Accepted file types; the content must also start with the matching signature.</summary>
    public static readonly IReadOnlyDictionary<string, byte[]> AllowedContentTypes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = "%PDF"u8.ToArray(),
        ["image/png"] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
        ["image/jpeg"] = [0xFF, 0xD8, 0xFF],
    };

    private RevisionReport()
    {
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    /// <summary>Day of the revision.</summary>
    public DateOnly InspectedOn { get; private set; }

    /// <summary>Name of the (external) revision technician.</summary>
    public string InspectorName { get; private set; } = string.Empty;

    /// <summary>Number of the revision technician's certificate (osvedčenie), optional.</summary>
    public string? InspectorCertificateNumber { get; private set; }

    public RevisionResult Result { get; private set; }

    /// <summary>Last day the revision is valid (date of the next periodic revision); required for a passed revision.</summary>
    public DateOnly? ValidUntil { get; private set; }

    /// <summary>Defects found (required for a failed revision) or remarks.</summary>
    public string? Findings { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>SHA-256 of the file (lowercase hex) to prove the stored report was not altered.</summary>
    public string Sha256 { get; private set; } = string.Empty;

    /// <summary>Raw file bytes. Never part of a response other than the download.</summary>
    public byte[] Content { get; private set; } = [];

    public Guid RecordedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public static RevisionReport Create(
        Guid installationId,
        DateOnly inspectedOn,
        string inspectorName,
        string? inspectorCertificateNumber,
        RevisionResult result,
        DateOnly? validUntil,
        string? findings,
        string fileName,
        string contentType,
        byte[] content,
        Guid recordedBy,
        DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        InstallationId = installationId,
        InspectedOn = inspectedOn,
        InspectorName = inspectorName.Trim(),
        InspectorCertificateNumber = string.IsNullOrWhiteSpace(inspectorCertificateNumber) ? null : inspectorCertificateNumber.Trim(),
        Result = result,
        ValidUntil = validUntil,
        Findings = string.IsNullOrWhiteSpace(findings) ? null : findings.Trim(),
        FileName = SafeFileName(fileName),
        ContentType = contentType.Trim().ToLowerInvariant(),
        SizeBytes = content.LongLength,
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
        Content = content,
        RecordedBy = recordedBy,
        RecordedAt = now,
    };

    /// <summary>True when <paramref name="content"/> starts with the signature of the declared <paramref name="contentType"/>.</summary>
    public static bool HasValidSignature(string contentType, ReadOnlySpan<byte> content) =>
        AllowedContentTypes.TryGetValue(contentType.Trim(), out var signature) && content.StartsWith(signature);

    // Keeps only the last path segment, so a client cannot smuggle a path into the download header.
    private static string SafeFileName(string fileName)
    {
        var name = fileName.Trim().Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Trim();
        return string.IsNullOrEmpty(name) ? "revizna-sprava" : name;
    }
}
