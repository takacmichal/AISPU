using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>
/// Predicted production of one hour of an installation. The newest run overwrites the hour, whatever the model version.
/// GHI and cloud cover are copied from the forecast because forecast rows are replaced daily, while the detection reads
/// them after the hour has passed.
/// </summary>
public sealed class StoredPrediction
{
    private StoredPrediction()
    {
        ModelVersion = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    /// <summary>Start of the hour, UTC.</summary>
    public DateTimeOffset HourStart { get; private set; }

    public double ExpectedPowerW { get; private set; }

    public double ExpectedEnergyWh { get; private set; }

    public double? GhiWm2 { get; private set; }

    public double? CloudCoverPct { get; private set; }

    public string ModelVersion { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    public static StoredPrediction Create(
        Guid installationId,
        DateTimeOffset hourStart,
        double expectedPowerW,
        double expectedEnergyWh,
        double? ghiWm2,
        double? cloudCoverPct,
        string modelVersion,
        DateTimeOffset generatedAt) => new()
        {
            Id = Guid.CreateVersion7(),
            InstallationId = installationId,
            HourStart = hourStart,
            ExpectedPowerW = expectedPowerW,
            ExpectedEnergyWh = expectedEnergyWh,
            GhiWm2 = ghiWm2,
            CloudCoverPct = cloudCoverPct,
            ModelVersion = modelVersion,
            GeneratedAt = generatedAt,
        };
}

internal sealed class StoredPredictionConfiguration : IEntityTypeConfiguration<StoredPrediction>
{
    public void Configure(EntityTypeBuilder<StoredPrediction> builder)
    {
        builder.ToTable("production_predictions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ModelVersion).HasMaxLength(100);

        // Read per installation and period; the unique index also guards the replace of an hour.
        builder.HasIndex(x => new { x.InstallationId, x.HourStart }).IsUnique();
    }
}
