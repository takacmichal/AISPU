using System.Text;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.ConnectionRequests;

public sealed class ConnectionRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    private static ConnectionAttachmentData Attachment(
        ConnectionAttachmentKind kind, string fileName = "podklad.pdf", string content = "PDF")
        => new(kind, fileName, "application/pdf", Encoding.UTF8.GetBytes(content));

    private static List<ConnectionAttachmentData> CompleteAttachments() =>
    [
        Attachment(ConnectionAttachmentKind.SingleLineDiagram, "schema.pdf"),
        Attachment(ConnectionAttachmentKind.OwnerConsent, "suhlas.pdf"),
    ];

    private static ConnectionRequest NewRequest(
        decimal powerKw = 9.6m, IReadOnlyList<ConnectionAttachmentData>? attachments = null) =>
        ConnectionRequest.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            powerKw,
            " Poznámka ",
            attachments ?? CompleteAttachments(),
            ConnectionRequest.DefaultSmallSourceMaxKw,
            User,
            Now);

    [Fact]
    public void Create_StartsReady_WithCopiedIdsAndAttachments()
    {
        var installationId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var supplyPointId = Guid.CreateVersion7();

        var request = ConnectionRequest.Create(
            installationId, customerId, supplyPointId, 9.6m, " Poznámka ",
            CompleteAttachments(), ConnectionRequest.DefaultSmallSourceMaxKw, User, Now);

        Assert.Equal(ConnectionRequestStatus.Ready, request.Status);
        Assert.Equal(ConnectionSourceType.SmallSource, request.Type);
        Assert.Equal(installationId, request.InstallationId);
        Assert.Equal(customerId, request.CustomerId);
        Assert.Equal(supplyPointId, request.SupplyPointId);
        Assert.Equal("Poznámka", request.Note);
        Assert.Equal(2, request.Attachments.Count);
        Assert.All(request.Attachments, a => Assert.True(a.SizeBytes > 0));
        Assert.Equal(User, request.CreatedBy);
        Assert.Equal(Now, request.CreatedAt);
    }

    [Theory]
    [InlineData("10.8", ConnectionSourceType.SmallSource)]
    [InlineData("10.801", ConnectionSourceType.LargeSource)]
    [InlineData("1", ConnectionSourceType.SmallSource)]
    [InlineData("100", ConnectionSourceType.LargeSource)]
    public void Create_DerivesTypeFromTotalInverterAcPower(string power, ConnectionSourceType expected)
    {
        var request = NewRequest(decimal.Parse(power, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected, request.Type);
        Assert.Equal(expected, ConnectionRequest.Classify(decimal.Parse(power, System.Globalization.CultureInfo.InvariantCulture), 10.8m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Classify_NonPositivePower_Throws(int power)
    {
        var ex = Assert.Throws<RequestValidationException>(() => ConnectionRequest.Classify(power, 10.8m));

        Assert.Contains(nameof(ConnectionRequest.TotalInverterAcPowerKw), ex.Errors.Keys);
    }

    [Fact]
    public void Create_WithoutAttachments_Throws_AndNamesBothMandatoryKinds()
    {
        var ex = Assert.Throws<RequestValidationException>(() => NewRequest(attachments: []));

        Assert.Contains("Attachments", ex.Errors.Keys);
        Assert.Contains("SingleLineDiagram", ex.Errors["Attachments"][0]);
        Assert.Contains("OwnerConsent", ex.Errors["Attachments"][0]);
    }

    [Fact]
    public void Create_WithOnlyDiagram_Throws_BecauseOwnerConsentIsMissing()
    {
        var attachments = new List<ConnectionAttachmentData>
        {
            Attachment(ConnectionAttachmentKind.SingleLineDiagram),
            Attachment(ConnectionAttachmentKind.InverterDatasheet),
        };

        var ex = Assert.Throws<RequestValidationException>(() => NewRequest(attachments: attachments));

        Assert.Contains("OwnerConsent", ex.Errors["Attachments"][0]);
    }

    [Fact]
    public void Create_TooManyAttachments_Throws()
    {
        var attachments = CompleteAttachments();
        while (attachments.Count <= ConnectionRequest.MaxAttachments)
        {
            attachments.Add(Attachment(ConnectionAttachmentKind.Other, $"dalsi-{attachments.Count}.pdf"));
        }

        var ex = Assert.Throws<RequestValidationException>(() => NewRequest(attachments: attachments));

        Assert.Contains("Attachments", ex.Errors.Keys);
    }

    [Fact]
    public void Create_EmptyAttachment_Throws()
    {
        var attachments = CompleteAttachments();
        attachments.Add(new ConnectionAttachmentData(ConnectionAttachmentKind.Other, "prazdny.pdf", "application/pdf", []));

        var ex = Assert.Throws<RequestValidationException>(() => NewRequest(attachments: attachments));

        Assert.Contains("Attachments[2].ContentBase64", ex.Errors.Keys);
    }

    [Fact]
    public void Update_ReplacesAttachments_RecalculatesType_AndKeepsReady()
    {
        var request = NewRequest();
        var updated = new List<ConnectionAttachmentData>
        {
            Attachment(ConnectionAttachmentKind.SingleLineDiagram, "nova-schema.pdf"),
            Attachment(ConnectionAttachmentKind.OwnerConsent, "novy-suhlas.pdf"),
            Attachment(ConnectionAttachmentKind.PanelDatasheet, "panel.pdf"),
        };

        request.Update(12.5m, null, updated, ConnectionRequest.DefaultSmallSourceMaxKw, Now.AddHours(1));

        Assert.Equal(ConnectionSourceType.LargeSource, request.Type);
        Assert.Equal(12.5m, request.TotalInverterAcPowerKw);
        Assert.Null(request.Note);
        Assert.Equal(3, request.Attachments.Count);
        Assert.Contains(request.Attachments, a => a.FileName == "nova-schema.pdf");
        Assert.Equal(ConnectionRequestStatus.Ready, request.Status);
        Assert.Equal(Now.AddHours(1), request.UpdatedAt);
    }

    [Fact]
    public void Update_WithoutMandatoryAttachment_Throws_AndKeepsOriginalAttachments()
    {
        var request = NewRequest();

        var ex = Assert.Throws<RequestValidationException>(() => request.Update(
            9.6m,
            null,
            [Attachment(ConnectionAttachmentKind.SingleLineDiagram)],
            ConnectionRequest.DefaultSmallSourceMaxKw,
            Now.AddHours(1)));

        Assert.Contains("OwnerConsent", ex.Errors["Attachments"][0]);
        Assert.Equal(2, request.Attachments.Count);
    }
}
