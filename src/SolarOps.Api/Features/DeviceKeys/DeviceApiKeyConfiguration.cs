using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SolarOps.Api.Features.Installations;

namespace SolarOps.Api.Features.DeviceKeys;

internal sealed class DeviceApiKeyConfiguration : IEntityTypeConfiguration<DeviceApiKey>
{
    public const string ActiveKeyIndexName = "ux_device_api_keys_active_installation";

    public void Configure(EntityTypeBuilder<DeviceApiKey> builder)
    {
        builder.ToTable("device_api_keys");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
        builder.Ignore(x => x.IsActive);

        // One active key per installation, enforced by the database (guards concurrent issue / rotate).
        builder.HasIndex(x => x.InstallationId).IsUnique().HasFilter("revoked_at IS NULL").HasDatabaseName(ActiveKeyIndexName);

        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
