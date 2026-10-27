using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.ConnectionRequests;

public sealed class ConnectionRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    private static AttachmentInput File(ConnectionRequestAttachmentKind kind, byte[]? content = null) =>
        new(kind, $"{kind}.pdf", "application/pdf", content?.LongLength ?? 1024, content);

    private static readonly AttachmentInput[] Required =
    [
        File(ConnectionRequestAttachmentKind.SingleLineDiagram),
        File(ConnectionRequestAttachmentKind.OwnerConsent),
    ];

    private static ConnectionRequest New(decimal acPowerKw = 8m, IReadOnlyCollection<AttachmentInput>? attachments = null) =>
        ConnectionRequest.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), " SK1234567890123456 ",
            acPowerKw, 10.8m, 25, attachments ?? Required, User, Now);

    [Fact]
    public void Create_StartsInDraft_LinkedToInstallation_WithAttachments()
    {
        var installationId = Guid.CreateVersion7();

        var request = ConnectionRequest.Create(
            installationId, Guid.CreateVersion7(), Guid.CreateVersion7(), "SK1234567890123456",
            8m, 10.8m, 25, Required, User, Now);

        Assert.Equal(ConnectionRequestState.Draft, request.State);
        Assert.Equal(installationId, request.InstallationId);
        Assert.Equal(2, request.Attachments.Count);
        Assert.Equal(Now, request.CreatedAt);
        Assert.Equal(User, request.CreatedBy);
    }

    [Theory]
    [InlineData(5, ConnectionRequestType.SmallSource)]
    [InlineData(10.8, ConnectionRequestType.SmallSource)]
    [InlineData(10.81, ConnectionRequestType.LargeSource)]
    [InlineData(50, ConnectionRequestType.LargeSource)]
    public void Create_DerivesTypeFromAcPower_With10Point8KwThreshold(double kw, ConnectionRequestType expected)
    {
        Assert.Equal(expected, New((decimal)kw).Type);
    }

    [Theory]
    [InlineData(ConnectionRequestAttachmentKind.SingleLineDiagram)]
    [InlineData(ConnectionRequestAttachmentKind.OwnerConsent)]
    public void Create_WithoutRequiredAttachment_Throws(ConnectionRequestAttachmentKind missing)
    {
        var attachments = Required.Where(a => a.Kind != missing).ToList();

        var ex = Assert.Throws<DomainException>(() => New(attachments: attachments));

        Assert.Contains(missing.ToString(), ex.Message);
    }

    [Fact]
    public void Create_TrimsEic_AndAcceptsOptionalAttachments()
    {
        var request = New(attachments: [.. Required, File(ConnectionRequestAttachmentKind.Datasheet)]);

        Assert.Equal("SK1234567890123456", request.EicCode);
        Assert.Equal(3, request.Attachments.Count);
    }

    [Fact]
    public void Create_WithContent_StoresBytesAndDerivesSize()
    {
        var content = "PDF content"u8.ToArray();
        var request = New(attachments: [
            File(ConnectionRequestAttachmentKind.SingleLineDiagram, content),
            File(ConnectionRequestAttachmentKind.OwnerConsent),
        ]);

        var diagram = Assert.Single(request.Attachments, a => a.Kind == ConnectionRequestAttachmentKind.SingleLineDiagram);
        Assert.True(diagram.HasContent);
        Assert.Equal(content, diagram.Content);
        Assert.Equal(content.LongLength, diagram.SizeBytes);

        var consent = Assert.Single(request.Attachments, a => a.Kind == ConnectionRequestAttachmentKind.OwnerConsent);
        Assert.False(consent.HasContent);
        Assert.Null(consent.Content);
        Assert.Equal(1024, consent.SizeBytes);
    }

    [Fact]
    public void Update_ReplacesFieldsAndAttachments_AndRecalculatesType()
    {
        var request = New();
        var content = "new diagram"u8.ToArray();

        request.Update(
            " SK9999999999999999 ",
            12.5m,
            10.8m,
            32,
            [
                File(ConnectionRequestAttachmentKind.SingleLineDiagram, content),
                File(ConnectionRequestAttachmentKind.OwnerConsent),
                File(ConnectionRequestAttachmentKind.Datasheet),
            ],
            Now.AddHours(1));

        Assert.Equal(ConnectionRequestState.Draft, request.State);
        Assert.Equal(ConnectionRequestType.LargeSource, request.Type);
        Assert.Equal("SK9999999999999999", request.EicCode);
        Assert.Equal(12.5m, request.RatedAcPowerKw);
        Assert.Equal(32, request.MainBreakerAmps);
        Assert.Equal(3, request.Attachments.Count);
        Assert.Contains(request.Attachments, a => a.Kind == ConnectionRequestAttachmentKind.Datasheet);
        Assert.Equal(Now.AddHours(1), request.UpdatedAt);
        var diagram = Assert.Single(request.Attachments, a => a.Kind == ConnectionRequestAttachmentKind.SingleLineDiagram);
        Assert.Equal(content, diagram.Content);
    }

    [Fact]
    public void Update_WithoutRequiredAttachment_Throws_AndKeepsOriginalAttachments()
    {
        var request = New();

        var ex = Assert.Throws<DomainException>(() => request.Update(
            "SK1234567890123456",
            8m,
            10.8m,
            25,
            [File(ConnectionRequestAttachmentKind.SingleLineDiagram)],
            Now.AddHours(1)));

        Assert.Contains(nameof(ConnectionRequestAttachmentKind.OwnerConsent), ex.Message);
        Assert.Equal(2, request.Attachments.Count);
    }
}
