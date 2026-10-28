using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>Connection request settings (<c>ConnectionRequest</c> section, ADR #203/#217): small source threshold and deadlines.</summary>
public sealed class ConnectionRequestOptions
{
    public const string SectionName = "ConnectionRequest";

    /// <summary>Largest rated AC power (kW) still handled as a small source.</summary>
    public decimal SmallSourceMaxKw { get; set; } = 10.8m;

    [ValidateObjectMembers]
    public SmallSourceDeadlines SmallSource { get; set; } = new();

    [ValidateObjectMembers]
    public LargeSourceDeadlines LargeSource { get; set; } = new();
}

/// <summary>Deadlines of a small source (up to 10.8 kW).</summary>
public sealed class SmallSourceDeadlines
{
    /// <summary>Working days the distributor has for the contract proposal (law 251/2012 §5g, default 10).</summary>
    [Range(1, 365)]
    public int ProposalWorkingDays { get; set; } = 10;
}

/// <summary>Deadlines of a larger source.</summary>
public sealed class LargeSourceDeadlines
{
    /// <summary>Calendar days the distributor has for the contract proposal (law 251/2012 §5c, default 30).</summary>
    [Range(1, 365)]
    public int ProposalCalendarDays { get; set; } = 30;
}
