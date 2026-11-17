using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PersonalFinance.Api.Services.Encryption;

// Envelope encryption (#120): every secret gets its own random 256-bit data
// key (DEK); the secret is encrypted with the DEK (AES-256-GCM), and the DEK
// is wrapped by the current key-encryption key (KEK). The stored value is
//
//   pfenc:v1:<kek id>:<wrapped DEK>:<nonce|ciphertext|tag>     (base64url parts)
//
// Rotating the KEK only re-wraps the small DEK (Rewrap) - the secret's own
// ciphertext is untouched and the plaintext never needs to exist in memory
// during rotation. The associated data ("context") binds a ciphertext to
// where it belongs (e.g. one user), so it can't be copied to another row
// and decrypted there.
public class EnvelopeEncryptor(IKeyEncryptionKeyProvider keys)
{
    public const string Prefix = "pfenc:v1:";
    private const int NonceSize = 12, TagSize = 16;

    public static bool IsEnvelope(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Encrypt(string plaintext, string context)
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var data = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[data.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(dek, TagSize))
                aes.Encrypt(nonce, data, ciphertext, tag, Encoding.UTF8.GetBytes(context));

            var keyId = keys.CurrentKeyId;
            return Format(keyId, keys.Wrap(keyId, dek), [.. nonce, .. ciphertext, .. tag]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public string Decrypt(string value, string context)
    {
        var (keyId, wrapped, payload) = Parse(value);
        var dek = keys.Unwrap(keyId, wrapped);
        try
        {
            if (payload.Length < NonceSize + TagSize) throw new CryptographicException("Ciphertext is too short.");
            var nonce = payload.AsSpan(0, NonceSize);
            var ciphertext = payload.AsSpan(NonceSize, payload.Length - NonceSize - TagSize);
            var tag = payload.AsSpan(payload.Length - TagSize);
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(dek, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(context));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    // The same secret, its DEK now wrapped by the current KEK. Unchanged if
    // it already is.
    public string Rewrap(string value)
    {
        var (keyId, wrapped, payload) = Parse(value);
        if (keyId == keys.CurrentKeyId) return value;

        var dek = keys.Unwrap(keyId, wrapped);
        try
        {
            return Format(keys.CurrentKeyId, keys.Wrap(keys.CurrentKeyId, dek), payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public static string? KeyIdOf(string value) => IsEnvelope(value) ? value[Prefix.Length..].Split(':')[0] : null;

    private static string Format(string keyId, byte[] wrapped, byte[] payload) =>
        $"{Prefix}{keyId}:{Base64UrlEncoder.Encode(wrapped)}:{Base64UrlEncoder.Encode(payload)}";

    private static (string KeyId, byte[] Wrapped, byte[] Payload) Parse(string value)
    {
        if (!IsEnvelope(value)) throw new CryptographicException("Not an envelope-encrypted value.");
        var parts = value[Prefix.Length..].Split(':');
        if (parts.Length != 3 || parts[0].Length == 0) throw new CryptographicException("Malformed envelope-encrypted value.");
        try
        {
            return (parts[0], Base64UrlEncoder.DecodeBytes(parts[1]), Base64UrlEncoder.DecodeBytes(parts[2]));
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Malformed envelope-encrypted value.", ex);
        }
    }
}
