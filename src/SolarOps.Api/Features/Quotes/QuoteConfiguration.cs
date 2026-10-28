using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SolarOps.Api.Features.Components;

namespace SolarOps.Api.Features.Quotes;

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("quotes");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedNever();

        builder.Property(q => q.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(q => q.VatRatePercent).HasPrecision(5, 2);
        builder.Property(q => q.Note).HasMaxLength(Quote.NoteMaxLength);
        builder.Property(q => q.NetTotal).HasPrecision(16, 2);
        builder.Property(q => q.VatTotal).HasPrecision(16, 2);
        builder.Property(q => q.GrossTotal).HasPrecision(16, 2);

        builder.HasIndex(q => q.CustomerId);

        // Cross-module reference by id only (no FK): the design lives in the FveDesigns module.
        builder.HasIndex(q => q.DesignId);
        builder.HasIndex(q => q.Status);

        builder.HasMany(q => q.Lines)
            .WithOne()
            .HasForeignKey(l => l.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(q => q.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(q => q.StateChanges)
            .WithOne()
            .HasForeignKey(c => c.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(q => q.StateChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(q => q.AllowedTransitions);
        builder.HasIndex(q => new { q.Status, q.ValidUntil });
    }
}

internal sealed class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine>
{
    public void Configure(EntityTypeBuilder<QuoteLine> builder)
    {
        builder.ToTable("quote_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.ComponentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Code).HasMaxLength(CatalogComponent.CodeMaxLength);
        builder.Property(l => l.Name).HasMaxLength(CatalogComponent.NameMaxLength);
        builder.Property(l => l.UnitPrice).HasPrecision(12, 2);
        builder.Property(l => l.Quantity).HasPrecision(10, 2);
        builder.Property(l => l.NetPrice).HasPrecision(16, 2);

        // Deliberately no foreign key to the catalog: the line is a snapshot and must outlive catalog changes.
        builder.HasIndex(l => new { l.QuoteId, l.Position }).IsUnique();
    }
}

internal sealed class QuoteStateChangeConfiguration : IEntityTypeConfiguration<QuoteStateChange>
{
    public void Configure(EntityTypeBuilder<QuoteStateChange> builder)
    {
        builder.ToTable("quote_state_changes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Note).HasMaxLength(Quote.NoteMaxLength);
        builder.HasIndex(c => new { c.QuoteId, c.ChangedAt });
    }
}
