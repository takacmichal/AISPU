using System.Security.Cryptography;
using System.Text;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.DeviceKeys;

/// <summary>
/// API key of an installation (#96): a gateway or the simulator sends it in the <c>X-Api-Key</c> header when it posts
/// telemetry. Only the SHA-256 hash of the secret is stored; the plaintext key is shown once when it is issued.
/// A SHA-256 hash is enough because the secret is 256 random bits (no password-style brute force); the key is
/// looked up by its id, so there is no scan over hashes. An installation has at most one active key.
/// </summary>
public sealed class DeviceApiKey
{
    public const string Prefix = "sok_";
    private const int SecretBytes = 32;

    private DeviceApiKey()
    {
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    /// <summary>Base64 of SHA-256 of the secret part of the key.</summary>
    public string KeyHash { get; private set; } = string.Empty;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public bool IsActive => RevokedAt is null;

    /// <summary>Creates a key and returns it together with the plaintext, which is not kept anywhere.</summary>
    public static (DeviceApiKey Key, string Plaintext) Issue(Guid installationId, Guid createdBy, DateTimeOffset now)
    {
        var id = Guid.CreateVersion7();
        var secret = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        var key = new DeviceApiKey
        {
            Id = id,
            InstallationId = installationId,
            KeyHash = Hash(secret),
            CreatedBy = createdBy,
            CreatedAt = now,
        };
        return (key, $"{Prefix}{id:N}_{secret}");
    }

    /// <summary>Splits <c>sok_&lt;id&gt;_&lt;secret&gt;</c>; false for any other shape.</summary>
    public static bool TryParse(string? apiKey, out Guid id, out string secret)
    {
        id = default;
        secret = string.Empty;
        if (apiKey is null || apiKey.Length > 200 || !apiKey.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = apiKey[Prefix.Length..].Split('_', 2);
        if (parts.Length != 2 || parts[0].Length != 32 || parts[1].Length == 0 || !Guid.TryParseExact(parts[0], "N", out id))
        {
            return false;
        }

        secret = parts[1];
        return true;
    }

    /// <summary>Constant-time comparison of the presented secret with the stored hash.</summary>
    public bool Matches(string secret) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(KeyHash));

    /// <summary>Revokes the key; a revoked key cannot be revoked again (<see cref="DomainException"/>, 409).</summary>
    public void Revoke(Guid by, DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw new DomainException("The API key has already been revoked.");
        }

        RevokedAt = now;
        RevokedBy = by;
    }

    private static string Hash(string secret) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
