# Key management

How stored secrets are encrypted, where the keys live, and how to rotate
them (#120). In short: **"encrypted at rest" with the key somewhere other
than the database, and a rotation that doesn't need the secrets decrypted.**

## What's protected

| Secret | Where | Bound to (associated data) |
|---|---|---|
| Plaid access tokens (long-lived bank access) | `Accounts.PlaidAccessToken` | the owning user |
| TOTP secrets (MFA, #117) | `AspNetUserTokens` (`AuthenticatorKey`) | the user and the token name |
| MFA recovery codes | `AspNetUserTokens` (`RecoveryCodes`) | the user and the token name |

Passwords are hashed by ASP.NET Identity (PBKDF2), and refresh tokens are
stored as SHA-256 hashes (#115). Neither is encrypted, because neither
ever needs to be read back.

## Envelope encryption

```
secret ──AES-256-GCM(DEK, associated data)──► ciphertext ─┐
DEK (random, per secret) ──AES-256-GCM(KEK)──► wrapped DEK ┼─► pfenc:v1:<kek id>:<wrapped DEK>:<nonce|ciphertext|tag>
KEK (from configuration, never in the DB) ─────────────────┘
```

- **Data key (DEK)**: 32 random bytes, a new one for every secret, zeroed
  after use. It encrypts the secret with AES-256-GCM, which gives integrity
  as well as secrecy: a flipped bit fails to decrypt.
- **Associated data**: authenticated but not encrypted. It ties each
  ciphertext to its owner, so a ciphertext copied into another user's row
  doesn't decrypt there.
- **Key-encryption key (KEK)**: wraps the DEK, also AES-256-GCM, with
  the KEK id as associated data so a wrapped key can't be relabelled.
  **The KEK is never stored in the database.** A database dump or backup on
  its own decrypts nothing.
- The KEK id is stored with each value, so several KEKs can be live during
  a rotation.

Code: `EnvelopeEncryptor`, `IKeyEncryptionKeyProvider`,
`LocalKeyEncryptionKeyProvider`, `PlaidTokenProtector`,
`ProtectedUserStore`.

## Where the KEK lives

`LocalKeyEncryptionKeyProvider` reads KEKs from configuration:

```
Encryption:CurrentKeyId = k1
Encryption:Keys:k1      = <base64, 32 bytes>     # scripts/new-kek.sh
```

| Environment | Put it in |
|---|---|
| Local dev | `dotnet user-secrets set "Encryption:Keys:k1" "$(scripts/new-kek.sh)"` and `... "Encryption:CurrentKeyId" "k1"` |
| Docker Compose | `.env` (`ENCRYPTION__KEYS__K1`), which is git-ignored |
| Azure Container Apps | a Container Apps **secret** referenced as an env var (`infra/main.bicep`). Encrypted by the platform, never in the image or the repo |

The API **refuses to start** without a valid current KEK, with a message
that points here.

### Production upgrade: a managed KMS

The interface is deliberately just `Wrap(keyId, dek)` / `Unwrap(keyId, wrapped)`.
That's exactly what Azure Key Vault (`wrapKey`/`unwrapKey`), AWS KMS
(`Encrypt`/`Decrypt`) and GCP KMS offer. A Key Vault provider would hold
only a key *name*. The KEK would never leave the HSM, every unwrap would
be authorised by the app's managed identity and audit-logged by Azure, and
rotation would be a new key version. No paid service is used today, so
the local provider stands in. Swapping it is one class and one DI line in
`AddPlaidIntegration`. Caching unwrapped DEKs briefly would keep KMS
calls (and cost) down.

## Rotating the KEK

Rotation never decrypts the secrets themselves. It only re-wraps each
small DEK, so it's fast and safe to re-run.

1. **Add the new key and make it current.** Keep the old one.
   ```bash
   dotnet user-secrets set "Encryption:Keys:k2" "$(scripts/new-kek.sh)"
   dotnet user-secrets set "Encryption:CurrentKeyId" "k2"
   ```
   Restart. New secrets use `k2` immediately, and old ones still decrypt
   with `k1`.
2. **Re-wrap everything onto the current key:**
   ```bash
   dotnet run --project src/PersonalFinance.Api -- rewrap-secrets
   ```
   ```
   Plaid tokens re-wrapped: 12
   Identity tokens re-wrapped: 4
   Failed: 0
   Secrets by key now: k2=16
   ```
   It's idempotent and exits non-zero if anything failed. It also upgrades
   Plaid tokens still in the pre-#120 Data Protection format (`CfDJ8…`) and
   any plain-text TOTP secrets written before this change. Accounts of one
   Plaid Item share one ciphertext, and they still do afterwards.
3. **Retire the old key** once `Secrets by key now` lists only the new one:
   remove `Encryption:Keys:k1` (or set it to empty) and restart.

If a key is suspected **compromised**, rotate the same way. Then also
treat the Plaid access tokens as exposed and re-link the banks
(`/item/access_token/invalidate` rotates a Plaid token without re-linking),
because anyone who had both the KEK and a database copy could have
decrypted them.

## Legacy: ASP.NET Data Protection

Plaid tokens stored before #120 used Data Protection, whose key ring lives
on disk unencrypted (`DataProtection:KeysPath`). Those tokens still
decrypt. Once `rewrap-secrets` reports no `legacy` values, the Data
Protection key ring is no longer needed for Plaid tokens.
