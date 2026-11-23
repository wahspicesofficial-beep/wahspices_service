using System.Security.Cryptography;

namespace WahSpices.API.Services;

/// <summary>
/// Customer return photos. Stored OUTSIDE wwwroot (never reachable as a static file) under
/// {ContentRoot}/App_Data/return-photos/{userId}/{random}.{ext}; served only through the authenticated return
/// endpoints (owner / admins with the Returns module). Config: ReturnPhotos:Root (optional absolute folder).
/// </summary>
public sealed class ReturnPhotoStore
{
    public const int MaxPhotos = 5;
    public const long MaxBytes = 5 * 1024 * 1024;
    private const string Prefix = "return-photos";
    private readonly string _root;          // …/App_Data
    private readonly ILogger<ReturnPhotoStore> _log;

    public ReturnPhotoStore(IWebHostEnvironment env, IConfiguration config, ILogger<ReturnPhotoStore> log)
    {
        _log = log;
        var configured = config["ReturnPhotos:Root"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(env.ContentRootPath, "App_Data") : configured);
        Directory.CreateDirectory(Path.Combine(_root, Prefix));
    }

    /// <summary>Validates every file first (count, size, magic bytes, parse), then writes them. Returns relative paths or an error.</summary>
    public async Task<(List<string> Paths, string? Error)> SaveAsync(int userId, IReadOnlyList<IFormFile> files, CancellationToken ct = default)
    {
        if (files.Count == 0) return (new(), null);
        if (files.Count > MaxPhotos) return (new(), $"You can attach up to {MaxPhotos} photos.");
        var clean = new List<ImageSanitizer.Result>();
        foreach (var f in files)
        {
            var name = Path.GetFileName(f.FileName ?? "photo");
            if (name.Length > 60) name = name[..60] + "…";
            if (f.Length <= 0) return (new(), $"'{name}' is empty.");
            if (f.Length > MaxBytes) return (new(), $"'{name}' is larger than 5 MB.");
            await using var s = f.OpenReadStream();
            using var ms = new MemoryStream((int)f.Length);
            await s.CopyToAsync(ms, ct);
            if (ms.Length > MaxBytes) return (new(), $"'{name}' is larger than 5 MB.");
            var img = ImageSanitizer.Sanitize(ms.ToArray());
            if (img == null) return (new(), $"'{name}' is not a valid JPG, PNG or WebP image.");
            if (img.Width > 12000 || img.Height > 12000) return (new(), $"'{name}' has unrealistic dimensions.");
            clean.Add(img);
        }

        var dir = Path.Combine(_root, Prefix, userId.ToString());
        Directory.CreateDirectory(dir);
        var paths = new List<string>();
        try
        {
            foreach (var img in clean)
            {
                var file = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + img.Extension;
                await using (var fs = new FileStream(Path.Combine(dir, file), FileMode.CreateNew, FileAccess.Write))
                    await fs.WriteAsync(img.Bytes, ct);
                paths.Add($"{Prefix}/{userId}/{file}");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Saving return photos failed for user {UserId}", userId);
            Delete(paths);
            return (new(), "Could not save your photos. Please try again.");
        }
        return (paths, null);
    }

    /// <summary>Absolute path for a stored relative path, or null when it is missing / escapes the store.</summary>
    public string? Resolve(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || !relative.StartsWith(Prefix + "/", StringComparison.Ordinal)) return null;
        var full = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var baseDir = Path.Combine(_root, Prefix) + Path.DirectorySeparatorChar;
        return full.StartsWith(baseDir, StringComparison.Ordinal) && File.Exists(full) ? full : null;
    }

    public void Delete(IEnumerable<string> relatives)
    {
        foreach (var r in relatives)
            try { if (Resolve(r) is { } p) File.Delete(p); } catch (Exception ex) { _log.LogWarning(ex, "Could not delete {Path}", r); }
    }
}

public static class ReturnPhotoResults
{
    /// <summary>Streams a stored photo with safe headers (nosniff, private cache, no script context).</summary>
    public static Microsoft.AspNetCore.Mvc.IActionResult Serve(this ReturnPhotoStore store, Microsoft.AspNetCore.Mvc.ControllerBase c, string? relative)
    {
        var full = store.Resolve(relative);
        if (full == null)
            return c.NotFound(WahSpices.Application.Common.ApiResponse<object>.Fail("NOT_FOUND", "Photo not found"));
        var h = c.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Cache-Control"] = "private, max-age=300";
        h["Content-Security-Policy"] = "default-src 'none'; sandbox";
        h["Cross-Origin-Resource-Policy"] = "same-site";
        return c.PhysicalFile(full, ImageSanitizer.ContentTypeFor(full));
    }
}
