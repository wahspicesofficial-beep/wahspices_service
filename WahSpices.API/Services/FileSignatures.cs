namespace WahSpices.API.Services;

/// <summary>
/// Content sniffing for uploads: the extension AND the file's leading "magic bytes" must agree.
/// Stops HTML/SVG/script files renamed to .jpg from being stored and later served from our origin.
/// SVG is deliberately not supported (it can carry script).
/// </summary>
public static class FileSignatures
{
    private static readonly Dictionary<string, Func<byte[], bool>> Checks = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"]  = IsJpeg, [".jpeg"] = IsJpeg,
        [".png"]  = b => StartsWith(b, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
        [".gif"]  = b => StartsWith(b, "GIF87a"u8) || StartsWith(b, "GIF89a"u8),
        [".webp"] = b => StartsWith(b, "RIFF"u8) && At(b, 8, "WEBP"u8),
        [".avif"] = b => At(b, 4, "ftyp"u8) && (At(b, 8, "avif"u8) || At(b, 8, "avis"u8) || At(b, 8, "mif1"u8)),
        [".pdf"]  = b => StartsWith(b, "%PDF-"u8),
        [".docx"] = b => StartsWith(b, 0x50, 0x4B, 0x03, 0x04),                                   // OOXML = zip
        [".doc"]  = b => StartsWith(b, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1),           // OLE2
        [".mp4"]  = IsIsoBmff, [".mov"] = IsIsoBmff,
        [".webm"] = b => StartsWith(b, 0x1A, 0x45, 0xDF, 0xA3), [".mkv"] = b => StartsWith(b, 0x1A, 0x45, 0xDF, 0xA3),
        [".avi"]  = b => StartsWith(b, "RIFF"u8) && At(b, 8, "AVI "u8),
    };

    public static bool IsSupported(string ext) => Checks.ContainsKey(ext);

    /// <summary>True when the stream's first bytes match the extension. Leaves the stream position at 0.</summary>
    public static async Task<bool> MatchesAsync(IFormFile file, string ext)
    {
        if (!Checks.TryGetValue(ext, out var check)) return false;
        var head = new byte[32];
        await using var s = file.OpenReadStream();
        var n = 0;
        while (n < head.Length)
        {
            var r = await s.ReadAsync(head.AsMemory(n));
            if (r == 0) break;
            n += r;
        }
        return n >= 4 && check(head[..n]);
    }

    /// <summary>Safe extension from a client file name: lower-case, one of the allowed set, else null.</summary>
    public static string? SafeExtension(string? fileName, IReadOnlySet<string> allowed)
    {
        var ext = Path.GetExtension(Path.GetFileName(fileName ?? "")).ToLowerInvariant();
        return ext.Length > 1 && allowed.Contains(ext) ? ext : null;
    }

    private static bool IsJpeg(byte[] b) => StartsWith(b, 0xFF, 0xD8, 0xFF);
    private static bool IsIsoBmff(byte[] b) => At(b, 4, "ftyp"u8);
    private static bool StartsWith(byte[] b, params byte[] sig) => b.Length >= sig.Length && b.AsSpan(0, sig.Length).SequenceEqual(sig);
    private static bool StartsWith(byte[] b, ReadOnlySpan<byte> sig) => b.Length >= sig.Length && b.AsSpan(0, sig.Length).SequenceEqual(sig);
    private static bool At(byte[] b, int offset, ReadOnlySpan<byte> sig) => b.Length >= offset + sig.Length && b.AsSpan(offset, sig.Length).SequenceEqual(sig);
}
