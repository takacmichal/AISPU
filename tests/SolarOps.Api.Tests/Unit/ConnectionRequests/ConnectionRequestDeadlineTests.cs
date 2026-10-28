using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.ConnectionRequests;

public sealed class ConnectionRequestDeadlineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 27);
    private static readonly ConnectionRequestOptions Options = new();

    private static ConnectionRequest New(decimal acPowerKw = 8m) =>
        ConnectionRequest.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "SK1234567890123456",
            acPowerKw, 10.8m, 25,
            [
                new AttachmentInput(ConnectionRequestAttachmentKind.SingleLineDiagram, "a.pdf", "application/pdf", 10),
                new AttachmentInput(ConnectionRequestAttachmentKind.OwnerConsent, "b.pdf", "application/pdf", 10),
            ],
            Guid.CreateVersion7(), Now);

    private static ConnectionRequest Submitted(decimal acPowerKw = 8m)
    {
        var request = New(acPowerKw);
        request.Submit(ConnectionRequestDeadlines.ProposalDueDate(request.Type, Today, Options), Now);
        return request;
    }

    [Fact]
    public void Options_DefaultToAdrDeadlines()
    {
        Assert.Equal(10, Options.SmallSource.ProposalWorkingDays);
        Assert.Equal(30, Options.LargeSource.ProposalCalendarDays);
    }

    [Fact]
    public void ProposalDueDate_SmallSource_UsesWorkingDays()
    {
        Assert.Equal(new DateOnly(2026, 11, 10), ConnectionRequestDeadlines.ProposalDueDate(ConnectionRequestType.SmallSource, Today, Options));
    }

    [Fact]
    public void ProposalDueDate_LargeSource_UsesCalendarDays()
    {
        Assert.Equal(new DateOnly(2026, 11, 26), ConnectionRequestDeadlines.ProposalDueDate(ConnectionRequestType.LargeSource, Today, Options));
    }

    [Fact]
    public void ProposalDueDate_FollowsConfiguration()
    {
        var options = new ConnectionRequestOptions
        {
            SmallSource = { ProposalWorkingDays = 5 },
            LargeSource = { ProposalCalendarDays = 15 },
        };

        Assert.Equal(new DateOnly(2026, 11, 3), ConnectionRequestDeadlines.ProposalDueDate(ConnectionRequestType.SmallSource, Today, options));
        Assert.Equal(new DateOnly(2026, 11, 11), ConnectionRequestDeadlines.ProposalDueDate(ConnectionRequestType.LargeSource, Today, options));
    }

    [Theory]
    [InlineData("2026-10-27", 30, false)]
    [InlineData("2026-11-26", 0, false)]
    [InlineData("2026-11-27", -1, true)]
    [InlineData("2026-12-01", -5, true)]
    public void Evaluate_SubmittedRequest_ReportsRemainingCalendarDaysAndOverdue(string today, int remaining, bool overdue)
    {
        var request = Submitted(12m); // large source, due 2026-11-26

        var status = ConnectionRequestDeadlines.Evaluate(request.State, request.ProposalDueDate, DateOnly.Parse(today));

        Assert.Equal(remaining, status.RemainingDays);
        Assert.Equal(overdue, status.IsOverdue);
    }

    [Theory]
    [InlineData(ConnectionRequestState.Draft)]
    [InlineData(ConnectionRequestState.InfoRequested)]
    [InlineData(ConnectionRequestState.Rejected)]
    [InlineData(ConnectionRequestState.ContractOffered)]
    public void Evaluate_NotAwaitingDistributor_HasNoDeadline(ConnectionRequestState state)
    {
        var status = ConnectionRequestDeadlines.Evaluate(state, new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1));

        Assert.Null(status.RemainingDays);
        Assert.False(status.IsOverdue);
    }

    [Fact]
    public void Submit_FromDraft_SetsSubmissionAndDueDate()
    {
        var request = New();
        var due = new DateOnly(2026, 11, 10);

        request.Submit(due, Now.AddHours(1));

        Assert.Equal(ConnectionRequestState.Submitted, request.State);
        Assert.Equal(Now.AddHours(1), request.SubmittedAt);
        Assert.Equal(due, request.ProposalDueDate);
        Assert.Equal(Now.AddHours(1), request.UpdatedAt);
    }

    [Fact]
    public void Submit_WhenNotDraft_Throws()
    {
        var request = Submitted();

        Assert.Throws<DomainException>(() => request.Submit(new DateOnly(2026, 11, 10), Now));
    }

    [Fact]
    public void RequestInfo_FromSubmitted_RecordsNote()
    {
        var request = Submitted();

        request.RequestInfo("Missing datasheet", Now.AddDays(2));

        Assert.Equal(ConnectionRequestState.InfoRequested, request.State);
        Assert.Equal("Missing datasheet", request.DecisionNote);
        Assert.Equal(Now.AddDays(2), request.DecidedAt);
    }

    [Fact]
    public void Reject_FromSubmitted_RecordsReason()
    {
        var request = Submitted();

        request.Reject("No grid capacity", Now.AddDays(2));

        Assert.Equal(ConnectionRequestState.Rejected, request.State);
        Assert.Equal("No grid capacity", request.DecisionNote);
    }

    [Fact]
    public void OfferContract_FromSubmitted_NoteOptional()
    {
        var request = Submitted();

        request.OfferContract(null, Now.AddDays(2));

        Assert.Equal(ConnectionRequestState.ContractOffered, request.State);
        Assert.Null(request.DecisionNote);
        Assert.Equal(Now.AddDays(2), request.DecidedAt);
    }

    [Fact]
    public void Decisions_FromOtherStates_Throw()
    {
        var draft = New();
        Assert.Throws<DomainException>(() => draft.RequestInfo("x", Now));
        Assert.Throws<DomainException>(() => draft.Reject("x", Now));
        Assert.Throws<DomainException>(() => draft.OfferContract(null, Now));

        var rejected = Submitted();
        rejected.Reject("x", Now);
        Assert.Throws<DomainException>(() => rejected.OfferContract(null, Now));
        Assert.Throws<DomainException>(() => rejected.RequestInfo("x", Now));
    }

    [Fact]
    public void Resubmit_FromInfoRequested_RestartsDeadline()
    {
        var request = Submitted();
        request.RequestInfo("Missing datasheet", Now.AddDays(2));
        var due = new DateOnly(2026, 11, 20);

        request.Resubmit(due, Now.AddDays(5));

        Assert.Equal(ConnectionRequestState.Submitted, request.State);
        Assert.Equal(Now.AddDays(5), request.SubmittedAt);
        Assert.Equal(due, request.ProposalDueDate);
        Assert.Null(request.DecisionNote);
        Assert.Null(request.DecidedAt);
    }

    [Fact]
    public void Resubmit_FromOtherStates_Throws()
    {
        Assert.Throws<DomainException>(() => New().Resubmit(new DateOnly(2026, 11, 20), Now));
        Assert.Throws<DomainException>(() => Submitted().Resubmit(new DateOnly(2026, 11, 20), Now));
    }

    [Fact]
    public void Update_IsAllowedWhileInfoRequested_ButNotAfterSubmission()
    {
        AttachmentInput[] files =
        [
            new(ConnectionRequestAttachmentKind.SingleLineDiagram, "a.pdf", "application/pdf", 10),
            new(ConnectionRequestAttachmentKind.OwnerConsent, "b.pdf", "application/pdf", 10),
        ];
        var request = Submitted();

        Assert.Throws<DomainException>(() => request.Update("SK1", 8m, 10.8m, null, files, Now));

        request.RequestInfo("fix EIC", Now);
        request.Update("SK1", 8m, 10.8m, null, files, Now);

        Assert.Equal("SK1", request.EicCode);
    }

    private static ConnectionRequest Offered()
    {
        var request = Submitted();
        request.OfferContract(null, Now);
        return request;
    }

    [Fact]
    public void ContractFlow_RunsInOrder()
    {
        var request = Offered();

        request.ReviewConditions(Now.AddDays(1));
        request.SendContract(Now.AddDays(2));
        request.CustomerSigned(Now.AddDays(3));
        request.SubmitSignedContract(Now.AddDays(4));

        Assert.Equal(ConnectionRequestState.ContractSubmitted, request.State);
        Assert.Equal(Now.AddDays(1), request.ConditionsReviewedAt);
        Assert.Equal(Now.AddDays(2), request.ContractSentAt);
        Assert.Equal(Now.AddDays(4), request.UpdatedAt);
    }

    [Fact]
    public void SendContract_WithoutReview_Throws() =>
        Assert.Throws<DomainException>(() => Offered().SendContract(Now));

    [Fact]
    public void CustomerSigned_WithoutSentContract_Throws()
    {
        var request = Offered();
        request.ReviewConditions(Now);

        Assert.Throws<DomainException>(() => request.CustomerSigned(Now));
    }

    [Fact]
    public void SubmitSignedContract_BeforeSignature_Throws() =>
        Assert.Throws<DomainException>(() => Offered().SubmitSignedContract(Now));

    [Fact]
    public void ContractSteps_OutsideContractOffered_Throw()
    {
        Assert.Throws<DomainException>(() => Submitted().ReviewConditions(Now));
        Assert.Throws<DomainException>(() => Submitted().SendContract(Now));
        Assert.Throws<DomainException>(() => Submitted().CustomerSigned(Now));
    }
}
