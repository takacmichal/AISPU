using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SolarOps.Api.Features.Components;

namespace SolarOps.Api.Features.FveDesigns;

public sealed class FveDesignVersionConfiguration : IEntityTypeConfiguration<FveDesignVersion>
{
    public const string VersionNumberIndex = "ux_fve_design_versions_design_id_version_number";

    public void Configure(EntityTypeBuilder<FveDesignVersion> builder)
    {
        builder.ToTable("fve_design_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.PanelCode).HasMaxLength(CatalogComponent.CodeMaxLength);
        builder.Property(v => v.PanelName).HasMaxLength(CatalogComponent.NameMaxLength);
        builder.Property(v => v.PanelUnitPrice).HasPrecision(12, 2);
        builder.Property(v => v.InverterCode).HasMaxLength(CatalogComponent.CodeMaxLength);
        builder.Property(v => v.InverterName).HasMaxLength(CatalogComponent.NameMaxLength);
        builder.Property(v => v.InverterUnitPrice).HasPrecision(12, 2);
        builder.Property(v => v.EstimateSource).HasMaxLength(50);

        // Same module: the design owns its versions. Panels and inverters are referenced by id only (snapshotted).
        builder.HasOne<FveDesign>().WithMany().HasForeignKey(v => v.DesignId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => new { v.DesignId, v.VersionNumber }).IsUnique().HasDatabaseName(VersionNumberIndex);
    }
}
