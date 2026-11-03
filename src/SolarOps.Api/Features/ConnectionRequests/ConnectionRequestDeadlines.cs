namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>Deadline state of a request: calendar days left until the due date (negative when late) and whether it is late.</summary>
public readonly record struct ProposalDeadlineStatus(int? RemainingDays, bool IsOverdue);

/// <summary>Deadline of the distributor for the contract proposal (ADR #203/#217), counted from the submission.</summary>
public static class ConnectionRequestDeadlines
{
    /// <summary>Small source: working days (default 10), large source: calendar days (default 30); both from <see cref="ConnectionRequestOptions"/>.</summary>
    public static DateOnly ProposalDueDate(ConnectionRequestType type, DateOnly submittedOn, ConnectionRequestOptions options) =>
        type == ConnectionRequestType.SmallSource
            ? WorkingCalendar.AddWorkingDays(submittedOn, options.SmallSource.ProposalWorkingDays)
            : submittedOn.AddDays(options.LargeSource.ProposalCalendarDays);

    /// <summary>The deadline only runs while the request waits for the distributor (<see cref="ConnectionRequestState.Submitted"/>).</summary>
    public static ProposalDeadlineStatus Evaluate(ConnectionRequestState state, DateOnly? dueDate, DateOnly today)
    {
        if (state != ConnectionRequestState.Submitted || dueDate is not { } due)
        {
            return new ProposalDeadlineStatus(null, false);
        }

        var remaining = due.DayNumber - today.DayNumber;
        return new ProposalDeadlineStatus(remaining, remaining < 0);
    }
}
