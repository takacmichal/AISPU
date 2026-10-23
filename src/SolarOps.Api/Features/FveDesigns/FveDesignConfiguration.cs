using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.FveDesigns;

public sealed class FveDesignConfiguration : IEntityTypeConfiguration<FveDesign>
{
    public void Configure(EntityTypeBuilder<FveDesign> builder)
    {
        builder.ToTable("fve_designs");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.ConsumptionProfile).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.RequestNotes).HasMaxLength(FveDesignLimits.NotesMaxLength);
        builder.Property(d => d.SiteSurveyNotes).HasMaxLength(FveDesignLimits.NotesMaxLength);
        builder.Property(d => d.InfeasibleReason).HasMaxLength(FveDesignLimits.NotesMaxLength);

        // Roof planes are always read and replaced together with the design, so they live in a jsonb column.
        builder.OwnsMany(d => d.RoofPlanes, plane =>
        {
            plane.ToJson();
            plane.Property(p => p.RoofType).HasConversion<string>();
        });

        // Cross-module references by id only (no FK): customers and supply points live in another module.
        builder.HasIndex(d => d.CustomerId);
        builder.HasIndex(d => d.SupplyPointId);
        builder.HasIndex(d => d.Status);
    }
}

public static class FveDesignLimits
{
    public const int NotesMaxLength = 4000;
    public const int NameMaxLength = 100;
    public const int MaxRoofPlanes = 20;
}
