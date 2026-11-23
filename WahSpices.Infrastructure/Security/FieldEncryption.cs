using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WahSpices.Domain.Entities;

namespace WahSpices.Infrastructure.Security;

/// <summary>
/// Field-level encryption at rest (AES-256-GCM).
///
/// Stored format:  enc:v1:{keyId}:{base64url(nonce[12] | tag[16] | ciphertext)}
///   - a fresh random 96-bit nonce per value (so equal plaintexts give different ciphertexts)
///   - the column name ("Employee.AadharNumber") is bound in as associated data, so a ciphertext copied into a
///     different column will not decrypt
///   - keyId lets keys rotate: new writes use the current key; old values still decrypt with a previous key and
///     the startup backfill re-encrypts them with the current one.
///
/// Key material comes ONLY from configuration (user-secrets in Development, environment variables in Production):
///   Security:DataEncryptionKey            base64 of 32 random bytes (current key)
///   Security:DataEncryptionKeyId          short id of the current key, default "k1"
///   Security:PreviousDataEncryptionKeys:{id}   base64 keys kept only for decrypting/rotating old values
/// </summary>
public sealed class FieldEncryptor
{
    public const string Prefix = "enc:v1:";
    private const int NonceSize = 12, TagSize = 16;

    private readonly Dictionary<string, byte[]> _keys;
    public string CurrentKeyId { get; }

    private FieldEncryptor(string currentKeyId, Dictionary<string, byte[]> keys)
    {
        CurrentKeyId = currentKeyId;
        _keys = keys;
    }

    /// <summary>The process-wide instance used by the EF value converters (the EF model is built once and cached).</summary>
    public static FieldEncryptor? Current { get; private set; }

    /// <summary>Raised (once per distinct problem) when a stored value can't be decrypted — wired to the logger at startup.</summary>
    public static Action<string>? OnDecryptFailure { get; set; }

    public static FieldEncryptor Configure(IConfiguration config)
    {
        var kid = (config["Security:DataEncryptionKeyId"] ?? "k1").Trim();
        if (kid.Length == 0 || kid.Length > 16 || kid.Any(c => !char.IsLetterOrDigit(c)))
            throw new InvalidOperationException("Security:DataEncryptionKeyId must be 1-16 letters/digits.");

        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [kid] = ParseKey(config["Security:DataEncryptionKey"], "Security:DataEncryptionKey"),
        };
        foreach (var prev in config.GetSection("Security:PreviousDataEncryptionKeys").GetChildren())
        {
            if (prev.Key == kid || string.IsNullOrWhiteSpace(prev.Value)) continue;
            keys[prev.Key] = ParseKey(prev.Value, $"Security:PreviousDataEncryptionKeys:{prev.Key}");
        }
        Current = new FieldEncryptor(kid, keys);
        return Current;
    }

    public static byte[] ParseKey(string? b64, string name)
    {
        if (string.IsNullOrWhiteSpace(b64))
            throw new InvalidOperationException($"{name} is not set. Generate one with `openssl rand -base64 32` and store it in user-secrets (Development) or an environment variable (Production). See SECURITY.md.");
        byte[] key;
        try { key = Convert.FromBase64String(b64.Trim()); }
        catch (FormatException) { throw new InvalidOperationException($"{name} is not valid base64."); }
        if (key.Length != 32) throw new InvalidOperationException($"{name} must decode to exactly 32 bytes (AES-256); got {key.Length}.");
        return key;
    }

    public static bool IsEncrypted(string? value) => value != null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Key id of an encrypted value, or null for plaintext.</summary>
    public static string? KeyIdOf(string? value)
    {
        if (!IsEncrypted(value)) return null;
        var rest = value!.AsSpan(Prefix.Length);
        var i = rest.IndexOf(':');
        return i <= 0 ? null : rest[..i].ToString();
    }

    public string Encrypt(string plaintext, string purpose)
    {
        if (IsEncrypted(plaintext)) return plaintext;   // idempotent: never double-encrypt
        var key = _keys[CurrentKeyId];
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var buf = new byte[NonceSize + TagSize + pt.Length];
        var nonce = buf.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using (var gcm = new AesGcm(key, TagSize))
            gcm.Encrypt(nonce, pt, buf.AsSpan(NonceSize + TagSize), buf.AsSpan(NonceSize, TagSize), Encoding.UTF8.GetBytes(purpose));
        CryptographicOperations.ZeroMemory(pt);
        return $"{Prefix}{CurrentKeyId}:{Base64UrlEncode(buf)}";
    }

    /// <summary>Plaintext for an encrypted value. Plaintext (legacy, not yet backfilled) values pass through unchanged.</summary>
    public bool TryDecrypt(string stored, string purpose, out string plaintext)
    {
        plaintext = stored;
        if (!IsEncrypted(stored)) return true;
        var rest = stored[Prefix.Length..];
        var sep = rest.IndexOf(':');
        if (sep <= 0) return false;
        var kid = rest[..sep];
        if (!_keys.TryGetValue(kid, out var key)) return false;
        byte[] buf;
        try { buf = Base64UrlDecode(rest[(sep + 1)..]); } catch (FormatException) { return false; }
        if (buf.Length < NonceSize + TagSize) return false;
        var pt = new byte[buf.Length - NonceSize - TagSize];
        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(buf.AsSpan(0, NonceSize), buf.AsSpan(NonceSize + TagSize), buf.AsSpan(NonceSize, TagSize), pt, Encoding.UTF8.GetBytes(purpose));
        }
        catch (CryptographicException) { return false; }
        plaintext = Encoding.UTF8.GetString(pt);
        return true;
    }

    // ── Static entry points used inside EF value-converter expressions ──
    public static string Protect(string value, string purpose)
        => (Current ?? throw new InvalidOperationException("Field encryption is not configured (Security:DataEncryptionKey)."))
            .Encrypt(value, purpose);

    public static string Unprotect(string value, string purpose)
    {
        if (!IsEncrypted(value)) return value;
        var enc = Current ?? throw new InvalidOperationException("Field encryption is not configured (Security:DataEncryptionKey).");
        if (enc.TryDecrypt(value, purpose, out var pt)) return pt;
        // Wrong/missing key or tampered value: hand back the ciphertext unchanged. Because Encrypt() is idempotent
        // on already-encrypted input, re-saving the entity keeps the original ciphertext instead of destroying it.
        OnDecryptFailure?.Invoke($"{purpose} (key id '{KeyIdOf(value)}')");
        return value;
    }

    private static string Base64UrlEncode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

/// <summary>Which columns are encrypted. Single source of truth for the EF converters and the backfill job.</summary>
public static class EncryptedFields
{
    public static readonly IReadOnlyDictionary<Type, string[]> Map = new Dictionary<Type, string[]>
    {
        [typeof(Employee)]         = [nameof(Employee.AadharNumber), nameof(Employee.PanNumber), nameof(Employee.BankAccountNo),
                                      nameof(Employee.BankIfsc), nameof(Employee.PfNumber), nameof(Employee.EsicNumber)],
        [typeof(EmployeeDocument)] = [nameof(EmployeeDocument.DocumentNumber)],
        [typeof(Vendor)]           = [nameof(Vendor.BankAccountNo), nameof(Vendor.BankIfsc)],
        [typeof(InvoiceSettings)]  = [nameof(InvoiceSettings.Pan), nameof(InvoiceSettings.BankAccount),
                                      nameof(InvoiceSettings.BankIfsc), nameof(InvoiceSettings.BankBranch)],
        [typeof(CrmCustomer)]      = [nameof(CrmCustomer.Pan)],
        [typeof(AdminUser)]        = [nameof(AdminUser.TotpSecret), nameof(AdminUser.TotpBackupCodes)],
        [typeof(ShippingProviderAccount)] = [nameof(ShippingProviderAccount.ApiPassword), nameof(ShippingProviderAccount.AuthToken),
                                             nameof(ShippingProviderAccount.WebhookToken)],
    };

    public static string Purpose(Type entity, string property) => $"{entity.Name}.{property}";
}

/// <summary>Display masks for sensitive identifiers — lists/details show these unless an audited "reveal" is used.</summary>
public static class SensitiveMask
{
    /// <summary>XXXX-XXXX-1234 for Aadhaar; generic "XXXXXX" + last 4 for everything else. Null/blank stays as is.</summary>
    public static string? Aadhaar(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return v;
        if (FieldEncryptor.IsEncrypted(v)) return "XXXX-XXXX-XXXX";
        var digits = new string(v.Where(char.IsLetterOrDigit).ToArray());
        return "XXXX-XXXX-" + Last(digits, 4);
    }

    public static string? Tail(string? v) => Tail(v, 4);

    public static string? Tail(string? v, int keep)
    {
        if (string.IsNullOrWhiteSpace(v)) return v;
        var s = v.Trim();
        if (FieldEncryptor.IsEncrypted(s)) return "XXXXXX";          // undecryptable — never show ciphertext tails
        return s.Length <= keep ? new string('X', s.Length) : new string('X', Math.Min(6, s.Length - keep)) + Last(s, keep);
    }

    /// <summary>True when the client sent back the masked form of the stored value (an edit form echoing what it was shown).</summary>
    public static bool IsEchoOfMask(string? incoming, string? stored, Func<string?, string?> mask)
        => incoming != null && incoming.Contains('X') && string.Equals(incoming.Trim(), mask(stored), StringComparison.Ordinal);

    private static string Last(string s, int n) => s.Length <= n ? s : s[^n..];
}
