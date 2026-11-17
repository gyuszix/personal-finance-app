namespace PersonalFinance.Api.Services.Encryption;

// Holds the key-encryption keys (KEKs) and does the only two things envelope
// encryption needs from them: wrap a data key, unwrap it again (#120). The
// KEK itself never leaves the provider - which is what lets a managed KMS
// (Azure Key Vault, AWS KMS, GCP KMS) implement this interface with
// wrapKey/unwrapKey calls instead, without the app ever holding the key.
public interface IKeyEncryptionKeyProvider
{
    // The KEK new data keys are wrapped with.
    string CurrentKeyId { get; }

    byte[] Wrap(string keyId, byte[] dataKey);

    // Throws CryptographicException for an unknown key or a wrapped key that
    // doesn't verify.
    byte[] Unwrap(string keyId, byte[] wrappedDataKey);
}
