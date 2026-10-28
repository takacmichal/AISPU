namespace SolarOps.Api.Features.DeviceKeys;

/// <summary>Metadata of an API key; the key itself is never returned again after it was issued.</summary>
public sealed record DeviceApiKeyResponse(
    Guid Id,
    Guid InstallationId,
    bool Active,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? RevokedAt,
    Guid? RevokedBy)
{
    internal static DeviceApiKeyResponse From(DeviceApiKey key) =>
        new(key.Id, key.InstallationId, key.IsActive, key.CreatedAt, key.CreatedBy, key.RevokedAt, key.RevokedBy);
}

/// <summary>A newly issued key. <c>apiKey</c> is the only time the plaintext is available: store it safely.</summary>
public sealed record IssuedDeviceApiKeyResponse(Guid Id, Guid InstallationId, string ApiKey, DateTimeOffset CreatedAt);
