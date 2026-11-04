namespace SolarOps.Api.Features.Installations;

/// <summary>Documents of the hand-over package (#93) that can be downloaded.</summary>
public enum InstallationDocumentKind
{
    RevisionReport,
    HandoverProtocol,
}

/// <summary>Audit row of one document download (#93: the download is recorded).</summary>
public sealed class InstallationDocumentDownload
{
    private InstallationDocumentDownload()
    {
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    public InstallationDocumentKind Kind { get; private set; }

    /// <summary>Downloaded document, e.g. the revision report id; null for the generated hand-over protocol.</summary>
    public Guid? DocumentId { get; private set; }

    public Guid DownloadedBy { get; private set; }

    public DateTimeOffset DownloadedAt { get; private set; }

    public static InstallationDocumentDownload Record(
        Guid installationId, InstallationDocumentKind kind, Guid? documentId, Guid by, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        InstallationId = installationId,
        Kind = kind,
        DocumentId = documentId,
        DownloadedBy = by,
        DownloadedAt = now,
    };
}
