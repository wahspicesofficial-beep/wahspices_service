namespace WahSpices.API.Services;

/// <summary>
/// Bills, receipts and licence documents for the company module. Same approach as staff documents: stored OUTSIDE wwwroot
/// under {ContentRoot}/App_Data/company-docs with a random name, referenced as "companydoc:{file}", downloaded only through
/// the authenticated admin endpoint. PDF / JPG / PNG / WEBP up to 10 MB, extension and magic bytes must agree.
/// </summary>
public sealed class CompanyDocStore
{
    public const string Prefix = "companydoc:";
    public const long MaxBytes = 10L * 1024 * 1024;
    private static readonly HashSet<string> AllowedExts = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };
    private readonly string _dir;

    public CompanyDocStore(IWebHostEnvironment env) => _dir = Path.Combine(env.ContentRootPath, "App_Data", "company-docs");

    public async Task<(string? Token, string? Error)> SaveAsync(IFormFile? file, string kind)
    {
        if (file == null || file.Length == 0) return (null, "Choose a file to upload.");
        var ext = FileSignatures.SafeExtension(file.FileName, AllowedExts);
        if (ext == null) return (null, "Only PDF, JPG, PNG or WEBP files are allowed.");
        if (file.Length > MaxBytes) return (null, "File must be 10 MB or smaller.");
        if (!await FileSignatures.MatchesAsync(file, ext)) return (null, "The file's contents don't match its type. Upload a real PDF or image.");
        Directory.CreateDirectory(_dir);
        var safeKind = new string(kind.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        var name = $"{safeKind}_{Guid.NewGuid():N}{ext}";
        await using (var fs = new FileStream(Path.Combine(_dir, name), FileMode.CreateNew, FileAccess.Write))
            await file.CopyToAsync(fs);
        return (Prefix + name, null);
    }

    /// <summary>Absolute path of a stored token, or null (not ours / missing).</summary>
    public string? Resolve(string? token)
    {
        if (string.IsNullOrEmpty(token) || !token.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var name = Path.GetFileName(token[Prefix.Length..]);
        var path = Path.Combine(_dir, name);
        return File.Exists(path) ? path : null;
    }

    public void Delete(string? token)
    {
        var p = Resolve(token);
        if (p != null) try { File.Delete(p); } catch (IOException) { /* best effort */ }
    }

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf", ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp",
        _ => "application/octet-stream",
    };
}
