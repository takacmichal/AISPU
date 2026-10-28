using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SolarOps.Api.Features.Customers;

namespace SolarOps.Api.Features.SupplyPoints;

internal sealed class SupplyPointConfiguration : IEntityTypeConfiguration<SupplyPoint>
{
    public void Configure(EntityTypeBuilder<SupplyPoint> builder)
    {
        builder.ToTable("supply_points");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(SupplyPoint.NameMaxLength);
        builder.Property(x => x.EicCode).HasMaxLength(SupplyPoint.EicLength).IsRequired();

        builder.OwnsOne(x => x.Address, address =>
        {
            address.Property(a => a.Street).HasMaxLength(PostalAddress.StreetMaxLength);
            address.Property(a => a.City).HasMaxLength(PostalAddress.CityMaxLength);
            address.Property(a => a.PostalCode).HasMaxLength(PostalAddress.PostalCodeMaxLength);
            address.Property(a => a.Country).HasMaxLength(2);
        });
        builder.Navigation(x => x.Address).IsRequired();

        builder.HasIndex(x => x.EicCode).IsUnique();
        builder.HasIndex(x => x.CustomerId);

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
