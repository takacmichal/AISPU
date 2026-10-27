using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.ConnectionRequests;

internal sealed class ConnectionRequestConfiguration : IEntityTypeConfiguration<ConnectionRequest>
{
    public void Configure(EntityTypeBuilder<ConnectionRequest> builder)
    {
        builder.ToTable("connection_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EicCode).HasMaxLength(ConnectionRequest.EicMaxLength).IsRequired();
        builder.Property(x => x.RatedAcPowerKw).HasPrecision(10, 3);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(32);

        // Optimistic concurrency, so two edits cannot overwrite each other.
        builder.Property<uint>("xmin").IsRowVersion();

        // The installation is referenced by id only (modules do not share foreign keys); not unique, a rejected request may be followed by a new one.
        builder.HasIndex(x => x.InstallationId);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.State);

        builder.HasMany(x => x.Attachments)
            .WithOne()
            .HasForeignKey(x => x.ConnectionRequestId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ConnectionRequestAttachmentConfiguration : IEntityTypeConfiguration<ConnectionRequestAttachment>
{
    public void Configure(EntityTypeBuilder<ConnectionRequestAttachment> builder)
    {
        builder.ToTable("connection_request_attachments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.FileName).HasMaxLength(ConnectionRequest.FileNameMaxLength).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(ConnectionRequest.ContentTypeMaxLength).IsRequired();
        builder.Property(x => x.HasContent).IsRequired();
        builder.HasIndex(x => x.ConnectionRequestId);
    }
}
