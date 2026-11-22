namespace ProductShowcase.Api.Services;

public class LocalFileStorageService(IWebHostEnvironment environment, ILogger<LocalFileStorageService> logger)
    : IFileStorageService
{
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".jfif", ".avif", ".bmp"
    };

    private static readonly HashSet<string> AllowedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov"
    };

    private const long MaxImageSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const long MaxVideoSizeBytes = 100 * 1024 * 1024; // 100 MB

    public async Task<(string RelativeUrl, string MediaType)> SaveMediaFileAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            throw new ArgumentException("Lütfen geçerli bir fotoğraf veya video dosyası seçin.");
        }

        var extension = Path.GetExtension(file.FileName);
        var isImage = !string.IsNullOrWhiteSpace(extension) && AllowedImageExtensions.Contains(extension);
        var isVideo = !string.IsNullOrWhiteSpace(extension) && AllowedVideoExtensions.Contains(extension);

        if (!isImage && !isVideo)
        {
            throw new ArgumentException("Desteklenen formatlar: Fotoğraf (.jpg, .jpeg, .png, .webp, .gif, .jfif, .avif, .bmp) veya Video (.mp4, .webm, .mov).");
        }

        var maxLimit = isVideo ? MaxVideoSizeBytes : MaxImageSizeBytes;
        if (file.Length > maxLimit)
        {
            throw new ArgumentException(isVideo
                ? "Video boyutu en fazla 100 MB olabilir."
                : "Fotoğraf boyutu en fazla 10 MB olabilir.");
        }

        var relativeUrl = await SaveToUploadsAsync(file, extension, cancellationToken);
        return (relativeUrl, isVideo ? "video" : "image");
    }

    private async Task<string> SaveToUploadsAsync(IFormFile file, string extension, CancellationToken cancellationToken)
    {
        var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;

        var uploadsFolder = Path.Combine(webRoot, "uploads");
        Directory.CreateDirectory(uploadsFolder);

        var uniqueFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(uploadsFolder, uniqueFileName);

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await file.CopyToAsync(stream, cancellationToken);

        var relativeUrl = $"/uploads/{uniqueFileName}";
        logger.LogInformation("Medya dosyası kaydedildi: {RelativeUrl} ({Bytes} byte)", relativeUrl, file.Length);
        return relativeUrl;
    }

    public void DeleteFileIfExists(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || !relativePath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;

        var fullPath = Path.Combine(webRoot, "uploads", fileName);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            logger.LogInformation("Yüklenen medya silindi: {FilePath}", fullPath);
        }
    }
}
