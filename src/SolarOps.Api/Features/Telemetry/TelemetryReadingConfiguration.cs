using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.Telemetry;

internal sealed class TelemetryReadingConfiguration : IEntityTypeConfiguration<TelemetryReading>
{
    public const string InstallationTimeIndexName = "ix_telemetry_readings_installation_id_ts";
    public const string TimeBrinIndexName = "ix_telemetry_readings_ts_brin";

    public void Configure(EntityTypeBuilder<TelemetryReading> builder)
    {
        builder.ToTable("telemetry_readings");

        // (device_id, ts) is the idempotency key of the telemetry contract (ADR-185) and the primary key (ADR-220),
        // so a duplicate sample cannot be stored twice.
        builder.HasKey(x => new { x.DeviceId, x.Timestamp });
        builder.Property(x => x.DeviceId).ValueGeneratedNever();
        builder.Property(x => x.Timestamp).HasColumnName("ts").ValueGeneratedNever();

        builder.Property(x => x.IntervalSeconds).HasDefaultValue(TelemetryReading.DefaultIntervalSeconds);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

        // Per-installation range queries (e.g. the last 30 days of one installation) use this index directly.
        // ac_power_w is included so the usual chart query is answered by an index-only scan.
        builder.HasIndex(x => new { x.InstallationId, x.Timestamp })
            .IncludeProperties(x => x.AcPowerW)
            .HasDatabaseName(InstallationTimeIndexName);

        // BRIN is cheap for append-only time-series data and accelerates long time ranges (ADR-220).
        builder.HasIndex(x => x.Timestamp)
            .HasMethod("brin")
            .HasDatabaseName(TimeBrinIndexName);
    }
}
