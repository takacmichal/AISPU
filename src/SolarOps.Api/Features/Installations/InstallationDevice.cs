using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Installations;

/// <summary>Kind of a traced device (#83). Strings and panel layout are a separate story (#84).</summary>
public enum DeviceType
{
    Panel,
    Inverter,
}

public enum DeviceStatus
{
    Installed,
    Replaced,
}

/// <summary>
/// One physical panel or inverter of an installation, identified by a globally unique serial number.
/// A replaced device is never deleted: it is marked <see cref="DeviceStatus.Replaced"/> and linked to its successor,
/// so the replacement history stays available for service and warranty.
/// </summary>
public sealed class InstallationDevice
{
    public const int SerialNumberMaxLength = 100;
    public const int ManufacturerMaxLength = 100;
    public const int ModelMaxLength = 100;
    public const int ReasonMaxLength = 500;
    public const int MaxPowerW = 10_000_000;

    private InstallationDevice()
    {
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    public DeviceType Type { get; private set; }

    /// <summary>Trimmed, upper-cased and unique across all installations (also replaced devices).</summary>
    public string SerialNumber { get; private set; } = string.Empty;

    public string Manufacturer { get; private set; } = string.Empty;

    public string Model { get; private set; } = string.Empty;

    /// <summary>Rated power in watts (panel: peak power, inverter: nominal AC power).</summary>
    public int PowerW { get; private set; }

    public DeviceStatus Status { get; private set; }

    public Guid RegisteredBy { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public Guid? RemovedBy { get; private set; }

    public string? RemovalReason { get; private set; }

    /// <summary>The device that took the place of this one.</summary>
    public Guid? ReplacedByDeviceId { get; private set; }

    /// <summary>The device this one replaced.</summary>
    public Guid? ReplacesDeviceId { get; private set; }

    public static string NormalizeSerial(string serialNumber) => serialNumber.Trim().ToUpperInvariant();

    public static InstallationDevice Register(
        Guid installationId,
        DeviceType type,
        string serialNumber,
        string manufacturer,
        string model,
        int powerW,
        Guid registeredBy,
        DateTimeOffset now)
    {
        Validate(serialNumber, manufacturer, model, powerW, reason: null);
        return new InstallationDevice
        {
            Id = Guid.CreateVersion7(),
            InstallationId = installationId,
            Type = type,
            SerialNumber = NormalizeSerial(serialNumber),
            Manufacturer = manufacturer.Trim(),
            Model = model.Trim(),
            PowerW = powerW,
            Status = DeviceStatus.Installed,
            RegisteredBy = registeredBy,
            RegisteredAt = now,
        };
    }

    /// <summary>
    /// Marks this device as replaced and returns the new device of the same type. Throws
    /// <see cref="RequestValidationException"/> (400) for invalid data and <see cref="DomainException"/> (409)
    /// when the device has been replaced already. Nothing changes when it throws.
    /// </summary>
    public InstallationDevice Replace(
        string serialNumber,
        string manufacturer,
        string model,
        int powerW,
        string reason,
        Guid replacedBy,
        DateTimeOffset now)
    {
        Validate(serialNumber, manufacturer, model, powerW, reason);
        if (Status != DeviceStatus.Installed)
        {
            throw new DomainException($"Device '{SerialNumber}' has already been replaced.");
        }

        var replacement = Register(InstallationId, Type, serialNumber, manufacturer, model, powerW, replacedBy, now);
        replacement.ReplacesDeviceId = Id;

        Status = DeviceStatus.Replaced;
        RemovedAt = now;
        RemovedBy = replacedBy;
        RemovalReason = reason.Trim();
        ReplacedByDeviceId = replacement.Id;
        return replacement;
    }

    private static void Validate(string serialNumber, string manufacturer, string model, int powerW, string? reason)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(serialNumber) || serialNumber.Trim().Length > SerialNumberMaxLength)
        {
            errors["SerialNumber"] = [$"Serial number is required and at most {SerialNumberMaxLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(manufacturer) || manufacturer.Trim().Length > ManufacturerMaxLength)
        {
            errors["Manufacturer"] = [$"Manufacturer is required and at most {ManufacturerMaxLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(model) || model.Trim().Length > ModelMaxLength)
        {
            errors["Model"] = [$"Model is required and at most {ModelMaxLength} characters."];
        }

        if (powerW is <= 0 or > MaxPowerW)
        {
            errors["PowerW"] = [$"Power in watts must be between 1 and {MaxPowerW}."];
        }

        if (reason is not null && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > ReasonMaxLength))
        {
            errors["Reason"] = [$"Reason is required and at most {ReasonMaxLength} characters."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }
}
