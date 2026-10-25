using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.Components;

internal sealed class ComponentConfiguration : IEntityTypeConfiguration<CatalogComponent>
{
    public void Configure(EntityTypeBuilder<CatalogComponent> builder)
    {
        builder.ToTable("catalog_components");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Code).HasMaxLength(CatalogComponent.CodeMaxLength);
        builder.Property(c => c.Name).HasMaxLength(CatalogComponent.NameMaxLength);
        builder.Property(c => c.Manufacturer).HasMaxLength(CatalogComponent.ManufacturerMaxLength);
        builder.Property(c => c.UnitPrice).HasPrecision(12, 2);

        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => new { c.Type, c.IsActive });
    }
}
