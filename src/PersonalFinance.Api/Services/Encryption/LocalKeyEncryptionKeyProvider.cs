using System.Security.Cryptography;
using System.Text;

namespace PersonalFinance.Api.Services.Encryption;

// KEKs from configuration - environment variables, user secrets, or a secret
// file the platform mounts - never from the database, so a copy of the
// database alone decrypts nothing (#120):
//
//   Encryption:CurrentKeyId = k2
//   Encryption:Keys:k1      = <base64 of 32 random bytes>   (retiring)
//   Encryption:Keys:k2      = <base64 of 32 random bytes>   (current)
//
// Data keys are wrapped with AES-256-GCM under the KEK, the key id bound in
// as associated data so a wrapped key can't be relabelled as another KEK's.
public class LocalKeyEncryptionKeyProvider : IKeyEncryptionKeyProvider
{
    private readonly Dictionary<string, byte[]> _keys;

    public LocalKeyEncryptionKeyProvider(IConfiguration config)
    {
        var section = config.GetSection("Encryption:Keys");
        // An empty value counts as absent, so a key can be retired by
        // blanking it in a layer that overrides the one that set it.
        _keys = section.GetChildren()
            .Where(k => !string.IsNullOrEmpty(k.Value))
            .ToDictionary(k => k.Key, k => Decode(k.Key, k.Value));
        CurrentKeyId = config["Encryption:CurrentKeyId"] ?? "";

        if (_keys.Count == 0 || !_keys.ContainsKey(CurrentKeyId))
            throw new InvalidOperationException(
                "No key-encryption key configured. Set Encryption:CurrentKeyId and Encryption:Keys:<id> to a base64 " +
                "32-byte key - `scripts/new-kek.sh` prints one. See docs/KEY_MANAGEMENT.md.");
    }

    public string CurrentKeyId { get; }

    public byte[] Wrap(string keyId, byte[] dataKey)
    {
        var kek = Key(keyId);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(kek, tag.Length);
        aes.Encrypt(nonce, dataKey, ciphertext, tag, Encoding.UTF8.GetBytes(keyId));
        return [.. nonce, .. ciphertext, .. tag];
    }

    public byte[] Unwrap(string keyId, byte[] wrappedDataKey)
    {
        var kek = Key(keyId);
        const int nonceSize = 12, tagSize = 16;
        if (wrappedDataKey.Length <= nonceSize + tagSize) throw new CryptographicException("Wrapped data key is too short.");
        var nonce = wrappedDataKey.AsSpan(0, nonceSize);
        var ciphertext = wrappedDataKey.AsSpan(nonceSize, wrappedDataKey.Length - nonceSize - tagSize);
        var tag = wrappedDataKey.AsSpan(wrappedDataKey.Length - tagSize);
        var dataKey = new byte[ciphertext.Length];
        using var aes = new AesGcm(kek, tagSize);
        aes.Decrypt(nonce, ciphertext, tag, dataKey, Encoding.UTF8.GetBytes(keyId));
        return dataKey;
    }

    private byte[] Key(string keyId) =>
        _keys.TryGetValue(keyId, out var key)
            ? key
            : throw new CryptographicException($"Key-encryption key '{keyId}' isn't configured (retired too early?).");

    private static byte[] Decode(string id, string? value)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(value ?? "");
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"Encryption:Keys:{id} isn't valid base64.");
        }
        return key.Length == 32 ? key : throw new InvalidOperationException($"Encryption:Keys:{id} must be 32 bytes (AES-256), not {key.Length}.");
    }
}
