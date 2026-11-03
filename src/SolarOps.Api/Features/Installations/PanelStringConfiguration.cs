using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.Installations;

internal sealed class PanelStringConfiguration : IEntityTypeConfiguration<PanelString>
{
    public const string NumberIndexName = "ux_panel_strings_installation_id_number";

    public void Configure(EntityTypeBuilder<PanelString> builder)
    {
        builder.ToTable("panel_strings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(PanelString.NameMaxLength);

        // A string number identifies the string within the installation (guards concurrent creation).
        builder.HasIndex(x => new { x.InstallationId, x.Number }).IsUnique().HasDatabaseName(NumberIndexName);

        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
