using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.Installations;

internal sealed class InstallationConfiguration : IEntityTypeConfiguration<Installation>
{
    public void Configure(EntityTypeBuilder<Installation> builder)
    {
        builder.ToTable("installations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(Installation.NameMaxLength).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(Installation.NoteMaxLength);
        builder.Property(x => x.PlannedPowerKwp).HasPrecision(10, 3);
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.TechnicianIds).IsRequired();

        // One installation per contract (#82: installation is bound to a contract and a supply point).
        builder.HasIndex(x => x.ContractId).IsUnique();
        builder.HasIndex(x => x.SupplyPointId);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.State);

        builder.Ignore(x => x.AllowedTransitions);
        builder.Ignore(x => x.IsClosed);

        builder.HasMany(x => x.StateChanges)
            .WithOne()
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.StateChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InstallationStateChangeConfiguration : IEntityTypeConfiguration<InstallationStateChange>
{
    public void Configure(EntityTypeBuilder<InstallationStateChange> builder)
    {
        builder.ToTable("installation_state_changes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FromState).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ToState).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Note).HasMaxLength(Installation.NoteMaxLength);
        builder.HasIndex(x => new { x.InstallationId, x.ChangedAt });
    }
}
