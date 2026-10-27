using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.Installations;

/// <summary>Identification and parameters of a device (shared by add and replace).</summary>
public abstract record DeviceSpecRequest : IValidatableObject
{
    /// <summary>Serial number from the label; unique across all installations, case-insensitive.</summary>
    [Required, MaxLength(InstallationDevice.SerialNumberMaxLength)]
    public string? SerialNumber { get; init; }

    [Required, MaxLength(InstallationDevice.ManufacturerMaxLength)]
    public string? Manufacturer { get; init; }

    [Required, MaxLength(InstallationDevice.ModelMaxLength)]
    public string? Model { get; init; }

    /// <summary>Rated power in watts.</summary>
    [Required, Range(1, InstallationDevice.MaxPowerW)]
    public int? PowerW { get; init; }

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (value, name) in new[]
                 {
                     (SerialNumber, nameof(SerialNumber)), (Manufacturer, nameof(Manufacturer)), (Model, nameof(Model)),
                 })
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                yield return new ValidationResult($"The {name} field is required.", [name]);
            }
        }
    }
}

/// <summary>Registers a panel or inverter on an installation.</summary>
public sealed record AddDeviceRequest : DeviceSpecRequest
{
    [Required]
    public DeviceType? Type { get; init; }
}

/// <summary>The device that takes the place of an installed one; it has the same type.</summary>
public sealed record ReplaceDeviceRequest : DeviceSpecRequest
{
    /// <summary>Why the device is replaced (fault, warranty claim, ...).</summary>
    [Required, MaxLength(InstallationDevice.ReasonMaxLength)]
    public string? Reason { get; init; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        if (Reason is not null && string.IsNullOrWhiteSpace(Reason))
        {
            yield return new ValidationResult("The Reason field is required.", [nameof(Reason)]);
        }
    }
}

public sealed record DeviceResponse(
    Guid Id,
    Guid InstallationId,
    DeviceType Type,
    string SerialNumber,
    string Manufacturer,
    string Model,
    int PowerW,
    DeviceStatus Status,
    Guid RegisteredBy,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? RemovedAt,
    Guid? RemovedBy,
    string? RemovalReason,
    Guid? ReplacedByDeviceId,
    Guid? ReplacesDeviceId)
{
    internal static DeviceResponse From(InstallationDevice x) => new(
        x.Id, x.InstallationId, x.Type, x.SerialNumber, x.Manufacturer, x.Model, x.PowerW, x.Status, x.RegisteredBy,
        x.RegisteredAt, x.RemovedAt, x.RemovedBy, x.RemovalReason, x.ReplacedByDeviceId, x.ReplacesDeviceId);
}

/// <summary>Result of a replacement: the device taken out and the one installed in its place.</summary>
public sealed record DeviceReplacementResponse(DeviceResponse Replaced, DeviceResponse Replacement);
