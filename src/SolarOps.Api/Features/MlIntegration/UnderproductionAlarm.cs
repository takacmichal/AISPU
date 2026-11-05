using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>Severity of an underproduction event (ADR #194).</summary>
public enum UnderproductionSeverity
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Underproduction event reported by the detection of the ML service (ADR #194). The id comes from the service and is
/// stable for the same installation and start, so a repeated run updates the alarm instead of adding another one.
/// </summary>
public sealed class UnderproductionAlarm
{
    private UnderproductionAlarm()
    {
        ReasonCode = Reason = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    /// <summary>End of the first underproducing hour (the service labels hours by their end).</summary>
    public DateTimeOffset Start { get; private set; }

    /// <summary>Null while the alarm is ongoing.</summary>
    public DateTimeOffset? End { get; private set; }

    public DateTimeOffset DetectedAt { get; private set; }

    public DateTimeOffset LastUnderproductionAt { get; private set; }

    public bool Ongoing { get; private set; }

    public UnderproductionSeverity Severity { get; private set; }

    /// <summary>Priority the service recommends for the ticket (#33).</summary>
    public ServiceTicketPriority TicketPriority { get; private set; }

    public double MedianRatio { get; private set; }

    public double MinRatio { get; private set; }

    public int UnderproducingHours { get; private set; }

    public int ValidHours { get; private set; }

    public double DurationHours { get; private set; }

    public double ExpectedKwh { get; private set; }

    public double ActualKwh { get; private set; }

    public double EstimatedLostKwh { get; private set; }

    public string ReasonCode { get; private set; }

    public string Reason { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal static UnderproductionAlarm Create(Guid installationId, MlAlarm alarm, DateTimeOffset now)
    {
        var entity = new UnderproductionAlarm { Id = alarm.AlarmId, InstallationId = installationId };
        entity.Apply(alarm, now);
        return entity;
    }

    internal void Apply(MlAlarm alarm, DateTimeOffset now)
    {
        Start = alarm.Start;
        End = alarm.End;
        DetectedAt = alarm.DetectedAt;
        LastUnderproductionAt = alarm.LastUnderproductionAt;
        Ongoing = alarm.Ongoing;
        Severity = alarm.Severity;
        TicketPriority = alarm.TicketPriority;
        MedianRatio = alarm.MedianRatio;
        MinRatio = alarm.MinRatio;
        UnderproducingHours = alarm.UnderproducingHours;
        ValidHours = alarm.ValidHours;
        DurationHours = alarm.DurationHours;
        ExpectedKwh = alarm.ExpectedKwh;
        ActualKwh = alarm.ActualKwh;
        EstimatedLostKwh = alarm.EstimatedLostKwh;
        ReasonCode = alarm.ReasonCode;
        Reason = alarm.Reason;
        UpdatedAt = now;
    }
}

internal sealed class UnderproductionAlarmConfiguration : IEntityTypeConfiguration<UnderproductionAlarm>
{
    public void Configure(EntityTypeBuilder<UnderproductionAlarm> builder)
    {
        builder.ToTable("underproduction_alarms");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.TicketPriority).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ReasonCode).HasMaxLength(100);
        builder.Property(x => x.Reason).HasMaxLength(1000);

        builder.HasIndex(x => new { x.InstallationId, x.Start });
    }
}
