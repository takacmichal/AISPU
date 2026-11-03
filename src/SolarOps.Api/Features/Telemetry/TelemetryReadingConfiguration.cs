using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.Telemetry;

/// <summary>
/// Table <c>telemetry_readings</c> (ADR #220). Column names are set explicitly because the ingest service
/// inserts with raw SQL and must match them.
/// </summary>
internal sealed class TelemetryReadingConfiguration : IEntityTypeConfiguration<TelemetryReading>
{
    public const string TimestampIndexName = "ix_telemetry_readings_ts_brin";

    public void Configure(EntityTypeBuilder<TelemetryReading> builder)
    {
        builder.ToTable("telemetry_readings");
        // The primary key is the idempotency key (deviceId, timestamp) and stays compatible with partitioning by ts.
        builder.HasKey(x => new { x.DeviceId, x.Timestamp });

        builder.Property(x => x.DeviceId).HasColumnName("device_id");
        builder.Property(x => x.Timestamp).HasColumnName("ts").HasColumnType("timestamp with time zone");
        builder.Property(x => x.IntervalSec).HasColumnName("interval_sec");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(x => x.VendorStatusCode).HasColumnName("vendor_status_code");
        builder.Property(x => x.EventsJson).HasColumnName("events").HasColumnType("jsonb");
        builder.Property(x => x.AcPowerW).HasColumnName("ac_power_w");
        builder.Property(x => x.DcPowerW).HasColumnName("dc_power_w");
        builder.Property(x => x.VoltageV).HasColumnName("voltage_v");
        builder.Property(x => x.CurrentA).HasColumnName("current_a");
        builder.Property(x => x.FrequencyHz).HasColumnName("frequency_hz");
        builder.Property(x => x.TemperatureC).HasColumnName("temperature_c");
        builder.Property(x => x.DayWh).HasColumnName("day_wh");
        builder.Property(x => x.TotalWh).HasColumnName("total_wh");
        builder.Property(x => x.StringsJson).HasColumnName("strings").HasColumnType("jsonb");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(16).IsRequired();
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamp with time zone");

        builder.HasIndex(x => x.Timestamp).HasMethod("brin").HasDatabaseName(TimestampIndexName);
    }
}
