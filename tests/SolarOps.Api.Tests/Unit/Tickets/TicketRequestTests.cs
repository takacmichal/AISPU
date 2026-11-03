using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.Tickets;

public sealed class TicketRequestTests
{
    private static readonly string Photo = Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0]);

    private static TicketAttachmentRequest File(string contentType = "image/jpeg", string? content = null) => new()
    {
        FileName = "porucha.jpg",
        ContentType = contentType,
        ContentBase64 = content ?? Photo,
    };

    private static CreateTicketRequest Request(params TicketAttachmentRequest[] attachments) => new()
    {
        InstallationId = Guid.CreateVersion7(),
        Title = "Invertor hlási chybu",
        Description = "Od rána nevyrába.",
        Attachments = attachments,
    };

    [Fact]
    public void Valid_HasNoErrors()
    {
        Assert.Empty(DataAnnotationsValidator.Validate(Request(File(), File("image/png"), File("application/pdf"))));
        Assert.Empty(DataAnnotationsValidator.Validate(Request() with { Attachments = null }));
    }

    [Fact]
    public void MissingFields_AreReported()
    {
        var errors = DataAnnotationsValidator.Validate(new CreateTicketRequest());

        Assert.Contains(nameof(CreateTicketRequest.InstallationId), errors.Keys);
        Assert.Contains(nameof(CreateTicketRequest.Title), errors.Keys);
        Assert.Contains(nameof(CreateTicketRequest.Description), errors.Keys);
    }

    [Fact]
    public void BlankTitle_AndEmptyInstallationId_AreErrors()
    {
        // IValidatableObject runs only after the attribute checks pass, so each case is validated separately.
        Assert.Contains(
            nameof(CreateTicketRequest.InstallationId),
            DataAnnotationsValidator.Validate(Request() with { InstallationId = Guid.Empty }).Keys);
        Assert.Contains(
            nameof(CreateTicketRequest.Title),
            DataAnnotationsValidator.Validate(Request() with { Title = "   " }).Keys);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/x-msdownload")]
    [InlineData("text/html")]
    [InlineData("image/jpeg; charset=utf-8")]
    public void AttachmentOfUnsupportedType_IsAnError(string contentType)
    {
        var errors = DataAnnotationsValidator.Validate(Request(File(contentType)));

        Assert.Contains("Attachments[0].ContentType", errors.Keys);
    }

    [Fact]
    public void TooManyAttachments_IsAnError()
    {
        var files = Enumerable.Range(0, ServiceTicket.MaxAttachments + 1).Select(_ => File()).ToArray();

        var errors = DataAnnotationsValidator.Validate(Request(files));

        Assert.Contains(nameof(CreateTicketRequest.Attachments), errors.Keys);
    }

    [Fact]
    public void AttachmentWithoutContent_IsAnError()
    {
        var errors = DataAnnotationsValidator.Validate(Request(File() with { ContentBase64 = null }));

        Assert.Contains("Attachments[0].ContentBase64", errors.Keys);
    }

    [Fact]
    public void UndefinedEnumValues_AreErrors()
    {
        Assert.Contains(
            nameof(CreateTicketRequest.Priority),
            DataAnnotationsValidator.Validate(Request() with { Priority = (ServiceTicketPriority)99 }).Keys);
        Assert.Contains(
            nameof(TransitionTicketRequest.State),
            DataAnnotationsValidator.Validate(new TransitionTicketRequest { State = (ServiceTicketState)99 }).Keys);
        Assert.Contains(
            nameof(TriageTicketRequest.Priority),
            DataAnnotationsValidator.Validate(new TriageTicketRequest()).Keys);
    }

    [Fact]
    public void Comment_RequiresBody()
    {
        Assert.Contains(nameof(AddTicketCommentRequest.Body), DataAnnotationsValidator.Validate(new AddTicketCommentRequest { Body = " " }).Keys);
        Assert.Empty(DataAnnotationsValidator.Validate(new AddTicketCommentRequest { Body = "OK", IsPublic = true }));
    }
}
