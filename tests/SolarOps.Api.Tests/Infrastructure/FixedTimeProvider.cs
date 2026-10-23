namespace SolarOps.Api.Tests.Infrastructure;

/// <summary>Controllable clock for tests (<see cref="Advance"/> moves time forward).</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
