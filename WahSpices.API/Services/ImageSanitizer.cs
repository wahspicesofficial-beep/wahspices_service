using System.Buffers.Binary;

namespace WahSpices.API.Services;

/// <summary>
/// Dependency-free image checks for customer uploads:
///  • the type is decided by MAGIC BYTES (never by file name / Content-Type) — JPEG, PNG or WebP only;
///  • the container is parsed end to end (a renamed script / HTML / polyglot fails to parse and is rejected);
///  • metadata is stripped: JPEG APP1 (EXIF incl. GPS / XMP), APP13 (IPTC), comments; PNG text / eXIf / tIME chunks;
///    WebP EXIF / XMP chunks. For JPEG the EXIF orientation is kept (as a minimal 1-tag EXIF block) so phone photos
///    are not shown sideways. Pixel data is copied unchanged (no re-encoding library is referenced by the project).
/// </summary>
public static class ImageSanitizer
{
    public sealed record Result(string Extension, string ContentType, byte[] Bytes, int Width, int Height);

    public static Result? Sanitize(byte[] data)
    {
        try
        {
            if (data.Length > 12 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return Jpeg(data);
            if (data.Length > 33 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Png(data);
            if (data.Length > 20 && Ascii(data, 0, 4) == "RIFF" && Ascii(data, 8, 4) == "WEBP") return WebP(data);
        }
        catch (Exception) { /* malformed → rejected */ }
        return null;
    }

    public static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    private static string Ascii(byte[] d, int o, int n) => System.Text.Encoding.ASCII.GetString(d, o, n);

    // ── JPEG ────────────────────────────────────────────────────────────────
    private static Result? Jpeg(byte[] d)
    {
        using var ms = new MemoryStream(d.Length);
        ms.Write(d, 0, 2);                       // SOI
        int p = 2, w = 0, h = 0; ushort orientation = 0; bool sos = false, wroteOrientation = false;
        while (p + 4 <= d.Length)
        {
            if (d[p] != 0xFF) return null;
            var m = d[p + 1];
            if (m == 0xFF) { p++; continue; }    // fill byte
            if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { ms.Write(d, p, 2); p += 2; continue; }
            if (m == 0xD9) return null;          // EOI before any scan
            var len = (d[p + 2] << 8) | d[p + 3];
            if (len < 2 || p + 2 + len > d.Length) return null;
            if (m == 0xE1)                        // APP1 — EXIF / XMP: drop, remember orientation
            {
                if (orientation == 0) orientation = ExifOrientation(d, p + 4, len - 2);
            }
            else if (m == 0xED || m == 0xFE || (m >= 0xE3 && m <= 0xEC) || m == 0xEF)
            {
                // APP13 (IPTC / Photoshop), comments, vendor APP segments: dropped
            }
            else
            {
                if (m == 0xDA && !wroteOrientation && orientation is > 1 and <= 8) { WriteOrientation(ms, orientation); wroteOrientation = true; }
                if (m is >= 0xC0 and <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC && len >= 7)
                {
                    h = (d[p + 5] << 8) | d[p + 6];
                    w = (d[p + 7] << 8) | d[p + 8];
                }
                ms.Write(d, p, 2 + len);
                if (m == 0xDA)                   // start of scan: the rest is entropy-coded data up to EOI
                {
                    var rest = d.Length - (p + 2 + len);
                    ms.Write(d, p + 2 + len, rest);
                    sos = true;
                    break;
                }
            }
            p += 2 + len;
        }
        if (!sos || w <= 0 || h <= 0) return null;
        var outBytes = ms.ToArray();
        // must end with EOI (allow trailing padding zeros); anything appended after EOI (polyglots) is cut off
        var end = LastIndexOfEoi(outBytes);
        if (end < 0) return null;
        if (end + 2 < outBytes.Length) outBytes = outBytes[..(end + 2)];
        return new Result(".jpg", "image/jpeg", outBytes, w, h);
    }

    private static int LastIndexOfEoi(byte[] b)
    {
        for (var i = b.Length - 2; i >= Math.Max(0, b.Length - 1024 * 64); i--)
            if (b[i] == 0xFF && b[i + 1] == 0xD9) return i;
        return -1;
    }

    private static ushort ExifOrientation(byte[] d, int o, int n)
    {
        try
        {
            if (n < 14 || Ascii(d, o, 4) != "Exif") return 0;
            var t = o + 6;                       // TIFF header
            var le = d[t] == 0x49;               // "II" little endian, "MM" big endian
            ushort U16(int i) => le ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(i)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(i));
            uint U32(int i) => le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(i)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(i));
            var ifd = t + (int)U32(t + 4);
            if (ifd + 2 > o + n) return 0;
            int count = U16(ifd);
            for (var k = 0; k < count && ifd + 2 + k * 12 + 12 <= o + n; k++)
            {
                var e = ifd + 2 + k * 12;
                if (U16(e) == 0x0112) return U16(e + 8);
            }
        }
        catch (Exception) { }
        return 0;
    }

    /// <summary>APP1 with a minimal big-endian TIFF holding only tag 0x0112 (Orientation).</summary>
    private static void WriteOrientation(Stream s, ushort orientation)
    {
        var tiff = new byte[]
        {
            0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,     // MM, 42, IFD0 at 8
            0x00, 0x01,                                         // 1 entry
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01,     // Orientation, SHORT, count 1
            (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,                             // no next IFD
        };
        var payload = new byte[6 + tiff.Length];
        "Exif\0\0"u8.CopyTo(payload);
        tiff.CopyTo(payload, 6);
        var len = payload.Length + 2;
        s.Write(new byte[] { 0xFF, 0xE1, (byte)(len >> 8), (byte)len });
        s.Write(payload);
    }

    // ── PNG ─────────────────────────────────────────────────────────────────
    private static readonly HashSet<string> PngKeep = new(StringComparer.Ordinal)
        { "IHDR", "PLTE", "IDAT", "IEND", "tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "pHYs", "bKGD", "acTL", "fcTL", "fdAT" };

    private static Result? Png(byte[] d)
    {
        using var ms = new MemoryStream(d.Length);
        ms.Write(d, 0, 8);
        int p = 8, w = 0, h = 0; bool first = true, ended = false;
        while (p + 12 <= d.Length)
        {
            var len = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p));
            if (len < 0 || p + 12L + len > d.Length) return null;
            var type = Ascii(d, p + 4, 4);
            if (!type.All(char.IsAsciiLetter)) return null;
            if (first)
            {
                if (type != "IHDR" || len != 13) return null;
                w = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 8));
                h = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 12));
                first = false;
            }
            // keep every critical chunk (upper-case first letter) + the safe ancillary ones; drop tEXt/zTXt/iTXt/eXIf/tIME/…
            if (char.IsUpper(type[0]) || PngKeep.Contains(type)) ms.Write(d, p, 12 + len);
            p += 12 + len;
            if (type == "IEND") { ended = true; break; }   // trailing bytes after IEND are discarded
        }
        if (!ended || w <= 0 || h <= 0) return null;
        return new Result(".png", "image/png", ms.ToArray(), w, h);
    }

    // ── WebP ────────────────────────────────────────────────────────────────
    private static Result? WebP(byte[] d)
    {
        var riffLen = (long)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(4));
        var limit = (int)Math.Min(d.Length, 8 + riffLen);
        var chunks = new List<(string Type, int Offset, int Len)>();
        int p = 12;
        while (p + 8 <= limit)
        {
            var type = Ascii(d, p, 4);
            var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 4));
            if (len < 0 || p + 8L + len > limit) return null;
            chunks.Add((type, p, len));
            p += 8 + len + (len & 1);
        }
        if (chunks.Count == 0 || chunks[0].Type is not ("VP8 " or "VP8L" or "VP8X")) return null;
        int w = 0, h = 0;
        var c0 = chunks[0];
        if (c0.Type == "VP8X" && c0.Len >= 10)
        {
            w = 1 + (d[c0.Offset + 12] | d[c0.Offset + 13] << 8 | d[c0.Offset + 14] << 16);
            h = 1 + (d[c0.Offset + 15] | d[c0.Offset + 16] << 8 | d[c0.Offset + 17] << 16);
        }
        else if (c0.Type == "VP8L" && c0.Len >= 5 && d[c0.Offset + 8] == 0x2F)
        {
            var b = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(c0.Offset + 9));
            w = (int)(b & 0x3FFF) + 1; h = (int)((b >> 14) & 0x3FFF) + 1;
        }
        else if (c0.Type == "VP8 " && c0.Len >= 10)
        {
            w = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(c0.Offset + 14)) & 0x3FFF;
            h = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(c0.Offset + 16)) & 0x3FFF;
        }
        if (!chunks.Any(c => c.Type is "VP8 " or "VP8L" or "ANMF")) return null;

        using var body = new MemoryStream(d.Length);
        foreach (var c in chunks)
        {
            if (c.Type is "EXIF" or "XMP ") continue;
            var start = body.Position;
            body.Write(d, c.Offset, 8 + c.Len);
            if (c.Len % 2 == 1) body.WriteByte(0);          // RIFF chunks are padded to an even size
            if (c.Type == "VP8X")
            {
                var buf = body.GetBuffer();
                buf[start + 8] &= unchecked((byte)~0x0C);    // clear EXIF (0x08) + XMP (0x04) flags
            }
        }
        var bytes = new byte[12 + body.Length];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(4 + body.Length));
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        body.ToArray().CopyTo(bytes, 12);
        return w > 0 && h > 0 ? new Result(".webp", "image/webp", bytes, w, h) : null;
    }
}
