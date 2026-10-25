using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SolarOps.Api.Features.Components;

namespace SolarOps.Api.Features.Contracts;

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contracts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Sequence).UseSequence("contract_numbers");
        builder.Ignore(c => c.Number);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.VatRatePercent).HasPrecision(5, 2);
        builder.Property(c => c.NetTotal).HasPrecision(16, 2);
        builder.Property(c => c.VatTotal).HasPrecision(16, 2);
        builder.Property(c => c.GrossTotal).HasPrecision(16, 2);

        builder.HasIndex(c => c.Sequence).IsUnique();
        builder.HasIndex(c => c.QuoteId).IsUnique();
        builder.HasIndex(c => c.CustomerId);
        builder.HasIndex(c => c.Status);

        builder.HasMany(c => c.Lines)
            .WithOne()
            .HasForeignKey(l => l.ContractId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ContractLineConfiguration : IEntityTypeConfiguration<ContractLine>
{
    public void Configure(EntityTypeBuilder<ContractLine> builder)
    {
        builder.ToTable("contract_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.ComponentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Code).HasMaxLength(CatalogComponent.CodeMaxLength);
        builder.Property(l => l.Name).HasMaxLength(CatalogComponent.NameMaxLength);
        builder.Property(l => l.UnitPrice).HasPrecision(12, 2);
        builder.Property(l => l.Quantity).HasPrecision(10, 2);
        builder.Property(l => l.NetPrice).HasPrecision(16, 2);

        builder.HasIndex(l => new { l.ContractId, l.Position }).IsUnique();
    }
}
