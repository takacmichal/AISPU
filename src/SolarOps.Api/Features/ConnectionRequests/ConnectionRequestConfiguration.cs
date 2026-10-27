using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.ConnectionRequests;

internal sealed class ConnectionRequestConfiguration : IEntityTypeConfiguration<ConnectionRequest>
{
    public void Configure(EntityTypeBuilder<ConnectionRequest> builder)
    {
        builder.ToTable("connection_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.TotalInverterAcPowerKw).HasPrecision(10, 3);
        builder.Property(r => r.Note).HasMaxLength(ConnectionRequest.NoteMaxLength);

        // One connection request per installation; resubmission after a request for information stays on the same row (#86).
        builder.HasIndex(r => r.InstallationId).IsUnique();
        builder.HasIndex(r => r.CustomerId);
        builder.HasIndex(r => r.Status);

        builder.HasMany(r => r.Attachments)
            .WithOne()
            .HasForeignKey(a => a.ConnectionRequestId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Optimistic concurrency, so two edits cannot overwrite each other.
        builder.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class ConnectionRequestAttachmentConfiguration : IEntityTypeConfiguration<ConnectionRequestAttachment>
{
    public void Configure(EntityTypeBuilder<ConnectionRequestAttachment> builder)
    {
        builder.ToTable("connection_request_attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.FileName).HasMaxLength(ConnectionRequest.FileNameMaxLength).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(ConnectionRequest.ContentTypeMaxLength).IsRequired();
        builder.Property(a => a.Content).IsRequired();

        builder.HasIndex(a => a.ConnectionRequestId);
    }
}
