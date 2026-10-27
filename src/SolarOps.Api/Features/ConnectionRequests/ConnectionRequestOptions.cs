namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>Connection request settings (<c>ConnectionRequest</c> section, ADR #217). Deadlines follow with #86.</summary>
public sealed class ConnectionRequestOptions
{
    public const string SectionName = "ConnectionRequest";

    /// <summary>Largest rated AC power (kW) still handled as a small source.</summary>
    public decimal SmallSourceMaxKw { get; set; } = 10.8m;
}
