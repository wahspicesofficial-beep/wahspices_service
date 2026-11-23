using System.Security.Cryptography;

namespace WahSpices.Application.Services;

/// <summary>
/// RFC 6238 TOTP implementation — no external NuGet needed.
/// </summary>
public static class TotpService
{
    private const int Step = 30;     // 30-second window
    private const int Digits = 6;
    private const int Tolerance = 1; // ±1 step accepted

    // ── Generate a random Base32 secret ────────────────────────────────────
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32Encode(bytes);
    }

    // ── Build the otpauth:// URI for QR codes ──────────────────────────────
    public static string BuildQrUri(string secret, string email, string issuer = "WAH! Spices Admin")
    {
        var enc = Uri.EscapeDataString(issuer);
        var label = Uri.EscapeDataString($"{issuer}:{email}");
        return $"otpauth://totp/{label}?secret={secret}&issuer={enc}&algorithm=SHA1&digits=6&period=30";
    }

    // ── Verify a 6-digit code (±tolerance windows) ─────────────────────────
    public static bool Verify(string secret, string code)
    {
        if (code.Length != Digits || !code.All(char.IsDigit)) return false;
        var key = Base32Decode(secret);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Step;
        for (var i = -Tolerance; i <= Tolerance; i++)
        {
            if (ComputeTotp(key, now + i) == code) return true;
        }
        return false;
    }

    // ── Generate backup codes (8 × 8-char alphanumeric) ───────────────────
    public static string[] GenerateBackupCodes(int count = 8)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return Enumerable.Range(0, count).Select(_ =>
        {
            var b = RandomNumberGenerator.GetBytes(8);
            return new string(b.Select(x => chars[x % chars.Length]).ToArray());
        }).ToArray();
    }

    // ── Hash a backup code for storage ────────────────────────────────────
    public static string HashBackupCode(string code) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));

    // ── Verify a backup code against stored hashes ────────────────────────
    public static bool VerifyBackupCode(string code, IEnumerable<string> hashes) =>
        hashes.Contains(HashBackupCode(code.ToUpperInvariant()));

    // ── Internal ──────────────────────────────────────────────────────────
    private static string ComputeTotp(byte[] key, long counter)
    {
        var msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(msg);
        var offset = hash[^1] & 0x0F;
        var code = (((hash[offset] & 0x7F) << 24)
                  | ((hash[offset + 1] & 0xFF) << 16)
                  | ((hash[offset + 2] & 0xFF) << 8)
                  |  (hash[offset + 3] & 0xFF))
                  % (int)Math.Pow(10, Digits);
        return code.ToString().PadLeft(Digits, '0');
    }

    private static readonly char[] Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    private static string Base32Encode(byte[] data)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < data.Length; i += 5)
        {
            var block = new byte[5];
            int count = Math.Min(5, data.Length - i);
            Array.Copy(data, i, block, 0, count);
            sb.Append(Base32Chars[(block[0] & 0xF8) >> 3]);
            sb.Append(Base32Chars[((block[0] & 0x07) << 2) | ((block[1] & 0xC0) >> 6)]);
            if (count < 2) break;
            sb.Append(Base32Chars[(block[1] & 0x3E) >> 1]);
            sb.Append(Base32Chars[((block[1] & 0x01) << 4) | ((block[2] & 0xF0) >> 4)]);
            if (count < 3) break;
            sb.Append(Base32Chars[((block[2] & 0x0F) << 1) | ((block[3] & 0x80) >> 7)]);
            if (count < 4) break;
            sb.Append(Base32Chars[(block[3] & 0x7C) >> 2]);
            sb.Append(Base32Chars[((block[3] & 0x03) << 3) | ((block[4] & 0xE0) >> 5)]);
            if (count < 5) break;
            sb.Append(Base32Chars[block[4] & 0x1F]);
        }
        return sb.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        input = input.TrimEnd('=').ToUpperInvariant();
        var output = new byte[input.Length * 5 / 8];
        int buffer = 0, bitsLeft = 0, idx = 0;
        foreach (var c in input)
        {
            var val = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c);
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output[idx++] = (byte)(buffer >> (bitsLeft - 8));
                bitsLeft -= 8;
            }
        }
        return output;
    }
}
