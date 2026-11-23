using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace WahSpices.Infrastructure.Security;

/// <summary>Hashing / random helpers for secrets that must never be stored in plaintext.</summary>
public static class SecretHash
{
    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string RandomToken(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    public static bool FixedEquals(string? a, string? b)
    {
        if (a == null || b == null) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }

    /// <summary>Keyed sub-secret derived from the JWT signing key (used when no dedicated secret is configured).</summary>
    public static byte[] DeriveKey(IConfiguration config, string purpose, string? dedicatedKeyName = null)
    {
        var dedicated = dedicatedKeyName != null ? config[dedicatedKeyName] : null;
        var root = !string.IsNullOrWhiteSpace(dedicated) ? dedicated! : config["Jwt:SecretKey"] ?? "";
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(root), Encoding.UTF8.GetBytes("wah:" + purpose));
    }
}

/// <summary>Process-wide async lock per key (serialises refresh-token rotation per account).</summary>
public static class KeyedLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public static async Task<IDisposable> AcquireAsync(string key)
    {
        var sem = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        return new Releaser(sem);
    }

    private sealed class Releaser(SemaphoreSlim sem) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) sem.Release(); }
    }
}

/// <summary>
/// Refresh-token sessions stored (hashed) in the existing RefreshToken column as a small JSON array —
/// no schema change. Token format: "{ownerId}.{sessionKey}.{secret}"; only SHA-256(token) is persisted.
/// Rotation keeps the previous hash so a replayed (already rotated) token is recognised as REUSE.
/// </summary>
public sealed class RefreshSession
{
    [JsonPropertyName("k")] public string Key { get; set; } = "";          // family / session key
    [JsonPropertyName("h")] public string Hash { get; set; } = "";         // SHA-256 of the current token
    [JsonPropertyName("p")] public string? PrevHash { get; set; }          // SHA-256 of the previous token
    [JsonPropertyName("r")] public long RotatedAt { get; set; }            // unix seconds of last rotation
    [JsonPropertyName("c")] public long CreatedAt { get; set; }
    [JsonPropertyName("e")] public long ExpiresAt { get; set; }
}

public enum RefreshCheck { Valid, Grace, Reused, Invalid }

public static class RefreshSessions
{
    public const int MaxSessions = 10;
    /// <summary>A just-rotated token presented again within this window (two tabs refreshing at once) is not treated as theft.</summary>
    public const int GraceSeconds = 30;

    public static List<RefreshSession> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('[')) return new();   // legacy plaintext → no sessions
        try { return JsonSerializer.Deserialize<List<RefreshSession>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static string? Serialize(List<RefreshSession> list)
    {
        var now = Now;
        list.RemoveAll(s => s.ExpiresAt < now);
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static string NewToken(string ownerId, string key) => $"{ownerId}.{key}.{SecretHash.RandomToken(32)}";

    public static bool TryParseToken(string? token, out string ownerId, out string key)
    {
        ownerId = key = "";
        if (string.IsNullOrWhiteSpace(token) || token.Length > 300) return false;
        var parts = token.Split('.');
        if (parts.Length != 3) return false;
        ownerId = parts[0]; key = parts[1];
        return ownerId.Length > 0 && key.Length > 0;
    }

    /// <summary>Adds a new session (evicting the oldest beyond MaxSessions) and returns the raw token.</summary>
    public static string Create(List<RefreshSession> list, string ownerId, string key, TimeSpan lifetime)
    {
        var token = NewToken(ownerId, key);
        var now = Now;
        list.RemoveAll(s => s.ExpiresAt < now || s.Key == key);
        list.Add(new RefreshSession { Key = key, Hash = SecretHash.Sha256Hex(token), CreatedAt = now, RotatedAt = now,
                                      ExpiresAt = now + (long)lifetime.TotalSeconds });
        while (list.Count > MaxSessions) list.Remove(list.OrderBy(s => s.RotatedAt).First());
        return token;
    }

    public static RefreshCheck Check(List<RefreshSession> list, string key, string token, out RefreshSession? session)
    {
        session = list.FirstOrDefault(s => s.Key == key);
        if (session == null || session.ExpiresAt < Now) return RefreshCheck.Invalid;
        var h = SecretHash.Sha256Hex(token);
        if (SecretHash.FixedEquals(h, session.Hash)) return RefreshCheck.Valid;
        if (session.PrevHash != null && SecretHash.FixedEquals(h, session.PrevHash))
            return Now - session.RotatedAt <= GraceSeconds ? RefreshCheck.Grace : RefreshCheck.Reused;
        return RefreshCheck.Invalid;
    }

    /// <summary>Rotates the session: returns the new raw token. Sliding expiry capped by an absolute lifetime.</summary>
    public static string Rotate(RefreshSession s, string ownerId, TimeSpan sliding, TimeSpan absolute)
    {
        var token = NewToken(ownerId, s.Key);
        var now = Now;
        s.PrevHash = s.Hash;
        s.Hash = SecretHash.Sha256Hex(token);
        s.RotatedAt = now;
        s.ExpiresAt = Math.Min(now + (long)sliding.TotalSeconds, s.CreatedAt + (long)absolute.TotalSeconds);
        return token;
    }
}

/// <summary>
/// One-time codes stored as "h1$attempts$HMAC(identifier|purpose|code)". A plain hash of a 6-digit code
/// could be brute-forced offline from a DB dump, so the hash is keyed with a server secret.
/// </summary>
public static class OtpProtector
{
    public const int MaxAttempts = 5;

    public static string Protect(IConfiguration config, string identifier, string purpose, string code, int attempts = 0)
        => $"h1${attempts}${Mac(config, identifier, purpose, code)}";

    public static bool IsProtected(string stored) => stored.StartsWith("h1$");

    public static int Attempts(string stored)
    {
        var p = stored.Split('$');
        return p.Length == 3 && int.TryParse(p[1], out var n) ? n : 0;
    }

    public static string WithAttempts(string stored, int attempts)
    {
        var p = stored.Split('$');
        return p.Length == 3 ? $"h1${attempts}${p[2]}" : stored;
    }

    public static bool Matches(IConfiguration config, string stored, string identifier, string purpose, string code)
    {
        var p = stored.Split('$');
        if (p.Length != 3) return false;
        return SecretHash.FixedEquals(p[2], Mac(config, identifier, purpose, code));
    }

    private static string Mac(IConfiguration config, string identifier, string purpose, string code)
    {
        var key = SecretHash.DeriveKey(config, "otp", "Security:OtpPepper");
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{identifier.ToLowerInvariant()}|{purpose}|{code}")));
    }

    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}

/// <summary>Escalating lockout schedule shared by customer accounts (and the in-memory twin for unknown e-mails).</summary>
public static class LoginLockout
{
    public const int FreeAttempts = 5;

    /// <summary>Minutes to lock after the given number of consecutive failures (0 = no lock): 5→5, 6→10, 7→20, 8→40, 9+→60.</summary>
    public static int LockMinutes(int failures)
    {
        if (failures < FreeAttempts) return 0;
        var m = 5 * (1 << Math.Min(failures - FreeAttempts, 4));
        return Math.Min(m, 60);
    }
}

/// <summary>Password rules for customer accounts.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    /// <returns>null when acceptable, otherwise a user-facing reason.</returns>
    public static string? Validate(string? password, string? email = null, string? name = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Password must be at least {MinLength} characters.";
        if (password.Length > MaxLength)
            return $"Password must be at most {MaxLength} characters.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Password must contain both letters and numbers.";
        var lower = password.ToLowerInvariant();
        if (CommonPasswords.Set.Contains(lower) || CommonPasswords.Set.Contains(lower.TrimEnd('!', '@', '#', '.', '*')))
            return "This password is too common. Please choose a less guessable one.";
        if (password.Distinct().Count() <= 2)
            return "Password is too simple.";
        var local = email?.Split('@')[0].ToLowerInvariant();
        if (!string.IsNullOrEmpty(local) && local.Length >= 4 && lower.Contains(local))
            return "Password must not contain your email address.";
        return null;
    }
}
