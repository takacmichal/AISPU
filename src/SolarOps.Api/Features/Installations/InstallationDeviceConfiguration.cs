using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.Installations;

internal sealed class InstallationDeviceConfiguration : IEntityTypeConfiguration<InstallationDevice>
{
    public const string SerialIndexName = "ux_installation_devices_serial_number";
    public const string ReplacesIndexName = "ux_installation_devices_replaces_device_id";

    public void Configure(EntityTypeBuilder<InstallationDevice> builder)
    {
        builder.ToTable("installation_devices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.SerialNumber).HasMaxLength(InstallationDevice.SerialNumberMaxLength).IsRequired();
        builder.Property(x => x.Manufacturer).HasMaxLength(InstallationDevice.ManufacturerMaxLength).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(InstallationDevice.ModelMaxLength).IsRequired();
        builder.Property(x => x.RemovalReason).HasMaxLength(InstallationDevice.ReasonMaxLength);

        // The serial number is the identity of the physical unit: unique over active and replaced devices.
        builder.HasIndex(x => x.SerialNumber).IsUnique().HasDatabaseName(SerialIndexName);
        // A device can be replaced only once (guards a concurrent double replacement); nulls do not collide.
        builder.HasIndex(x => x.ReplacesDeviceId).IsUnique().HasDatabaseName(ReplacesIndexName);
        builder.HasIndex(x => new { x.InstallationId, x.Status });

        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
