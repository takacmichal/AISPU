using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.ConnectionRequests;

public sealed class ConnectionRequestRequestTests
{
    private static AttachmentRequest File(
        ConnectionRequestAttachmentKind kind,
        string contentType = "application/pdf",
        long sizeBytes = 1024,
        string? contentBase64 = null) => new()
        {
            Kind = kind,
            FileName = $"{kind}.pdf",
            ContentType = contentType,
            SizeBytes = sizeBytes,
            ContentBase64 = contentBase64,
        };

    private static CreateConnectionRequestRequest Request(params AttachmentRequest[] attachments) => new()
    {
        InstallationId = Guid.CreateVersion7(),
        EicCode = "24ZZS12345678901",
        RatedAcPowerKw = 8m,
        MainBreakerAmps = 25,
        Attachments = attachments,
    };

    private static AttachmentRequest[] Required(string contentType = "application/pdf") =>
    [
        File(ConnectionRequestAttachmentKind.SingleLineDiagram, contentType),
        File(ConnectionRequestAttachmentKind.OwnerConsent),
    ];

    [Fact]
    public void Valid_HasNoErrors()
    {
        Assert.Empty(DataAnnotationsValidator.Validate(Request(Required())));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("not a media type")]
    [InlineData("application/pdf; charset=utf-8")]
    [InlineData("*/*")]
    [InlineData("application/")]
    public void InvalidContentType_IsRejected(string contentType)
    {
        var errors = DataAnnotationsValidator.Validate(Request(Required(contentType)));

        Assert.Contains("Attachments[0].ContentType", errors.Keys);
    }

    [Fact]
    public void TooLongBase64_IsRejected()
    {
        var tooLong = new string('A', ConnectionRequest.MaxAttachmentBase64Length + 4);
        var errors = DataAnnotationsValidator.Validate(Request(
            File(ConnectionRequestAttachmentKind.SingleLineDiagram, sizeBytes: 0, contentBase64: tooLong),
            File(ConnectionRequestAttachmentKind.OwnerConsent)));

        Assert.Contains("Attachments[0].ContentBase64", errors.Keys);
    }

    [Fact]
    public void MissingRequiredAttachment_IsRejected()
    {
        var errors = DataAnnotationsValidator.Validate(Request(File(ConnectionRequestAttachmentKind.SingleLineDiagram)));

        Assert.Contains(nameof(CreateConnectionRequestRequest.Attachments), errors.Keys);
        Assert.Contains(
            nameof(ConnectionRequestAttachmentKind.OwnerConsent),
            errors[nameof(CreateConnectionRequestRequest.Attachments)].Single());
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public void CommonMediaTypes_AreAccepted(string contentType)
    {
        var errors = DataAnnotationsValidator.Validate(Request(Required(contentType)));

        Assert.DoesNotContain("Attachments[0].ContentType", errors.Keys);
    }
}
