namespace WahSpices.API.Services;

public record UploadResult(string Url, string FullPath);

public interface IFileUploadService
{
    Task<UploadResult> UploadAsync(IFormFile file, string subfolder);
    void DeleteByUrl(string? url);
}

public class FileUploadService : IFileUploadService
{
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _http;

    private static readonly HashSet<string> AllowedImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif" };

    private static readonly HashSet<string> AllowedVideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".webm", ".mov", ".avi", ".mkv" };

    private const long MaxImageBytes = 10L * 1024 * 1024;   // 10 MB
    private const long MaxVideoBytes = 200L * 1024 * 1024;  // 200 MB

    public FileUploadService(IWebHostEnvironment env, IHttpContextAccessor http)
    {
        _env  = env;
        _http = http;
    }

    private string WebRoot => _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");

    private static readonly HashSet<string> AllowedSubfolders = new(StringComparer.OrdinalIgnoreCase)
        { "blog", "recipes", "products", "marketing", "regions" };

    public async Task<UploadResult> UploadAsync(IFormFile file, string subfolder)
    {
        if (file == null || file.Length == 0)
            throw new InvalidOperationException("Choose a file to upload.");

        // Extension from the bare file name only (no path parts); SVG/HTML/anything else is rejected.
        var ext = Path.GetExtension(Path.GetFileName(file.FileName ?? "")).ToLowerInvariant();
        bool isVideo = AllowedVideoExts.Contains(ext);
        bool isImage = AllowedImageExts.Contains(ext);

        if (!isVideo && !isImage)
            throw new InvalidOperationException($"File type '{ext}' is not allowed. Use JPG, PNG, WEBP, GIF or AVIF images (or MP4/WEBM/MOV video).");

        long maxBytes = isVideo ? MaxVideoBytes : MaxImageBytes;
        if (file.Length > maxBytes)
            throw new InvalidOperationException(
                $"File size ({file.Length / (1024 * 1024)} MB) exceeds the {maxBytes / (1024 * 1024)} MB limit.");

        // The content must really be what the extension says (magic bytes) — stops renamed HTML/SVG/scripts.
        if (!await FileSignatures.MatchesAsync(file, ext))
            throw new InvalidOperationException("The file's contents don't match its type. Upload a real image or video file.");

        // Fixed set of public folders; random server-side file name (client name is never used on disk).
        var safe = System.Text.RegularExpressions.Regex.Replace(subfolder ?? "", @"[^a-zA-Z0-9_\-]", "");
        if (!AllowedSubfolders.Contains(safe))
            throw new InvalidOperationException("Invalid upload folder.");
        var uploadsRoot = Path.GetFullPath(Path.Combine(WebRoot, "uploads"));
        var dir  = Path.GetFullPath(Path.Combine(uploadsRoot, safe));
        if (!dir.StartsWith(uploadsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid upload folder.");
        Directory.CreateDirectory(dir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);

        await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write))
            await file.CopyToAsync(stream);

        var req      = _http.HttpContext?.Request;
        var baseUrl  = req != null ? $"{req.Scheme}://{req.Host}" : "";
        var relative = $"/uploads/{safe}/{fileName}";

        return new UploadResult($"{baseUrl}{relative}", fullPath);
    }

    public void DeleteByUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        string relative;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            relative = uri.AbsolutePath;
        else
            relative = url;

        if (!relative.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return;

        // Resolve and make sure the file really is inside wwwroot/uploads (no "../" escapes)
        var uploadsRoot = Path.GetFullPath(Path.Combine(WebRoot, "uploads")) + Path.DirectorySeparatorChar;
        var filePath = Path.GetFullPath(Path.Combine(WebRoot, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        if (!filePath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(filePath)) File.Delete(filePath);
    }
}
