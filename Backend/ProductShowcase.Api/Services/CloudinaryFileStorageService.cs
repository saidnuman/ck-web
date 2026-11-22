using System.Globalization;
using System.Text.RegularExpressions;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using ProductShowcase.Api.DTOs;

namespace ProductShowcase.Api.Services;

public class CloudinaryFileStorageService : IFileStorageService
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
    private const string DefaultFolder = "product-showcase";

    private readonly Cloudinary _cloudinary;
    private readonly string _folder;
    private readonly ILogger<CloudinaryFileStorageService> _logger;

    public CloudinaryFileStorageService(
        IConfiguration configuration,
        ILogger<CloudinaryFileStorageService> logger)
    {
        _logger = logger;

        var cloudName = configuration["Cloudinary:CloudName"]?.Trim();
        var apiKey = configuration["Cloudinary:ApiKey"]?.Trim();
        var apiSecret = configuration["Cloudinary:ApiSecret"]?.Trim();

        if (string.IsNullOrWhiteSpace(cloudName) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new InvalidOperationException("Cloudinary yapılandırması (CloudName, ApiKey, ApiSecret) eksik.");
        }

        _folder = string.IsNullOrWhiteSpace(configuration["Cloudinary:Folder"])
            ? DefaultFolder
            : configuration["Cloudinary:Folder"]!.Trim().Trim('/');

        var account = new Account(cloudName, apiKey, apiSecret);
        _cloudinary = new Cloudinary(account)
        {
            Api = { Secure = true }
        };
    }

    public async Task<(string RelativeUrl, string MediaType)> SaveMediaFileAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
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

        var uniqueFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using var stream = file.OpenReadStream();

        UploadResult uploadResult;
        if (isVideo)
        {
            var videoParams = new VideoUploadParams
            {
                File = new FileDescription(uniqueFileName, stream),
                Folder = _folder,
                UseFilename = false,
                UniqueFilename = true,
                Overwrite = false
            };

            uploadResult = file.Length > 20 * 1024 * 1024
                ? await _cloudinary.UploadLargeAsync(videoParams, cancellationToken: cancellationToken)
                : await _cloudinary.UploadAsync(videoParams, cancellationToken);
        }
        else
        {
            var imageParams = new ImageUploadParams
            {
                File = new FileDescription(uniqueFileName, stream),
                Folder = _folder,
                UseFilename = false,
                UniqueFilename = true,
                Overwrite = false
            };

            uploadResult = await _cloudinary.UploadAsync(imageParams, cancellationToken);
        }

        if (uploadResult.Error is not null || uploadResult.SecureUrl is null)
        {
            var errMsg = uploadResult.Error?.Message ?? "Cloudinary'den geçerli medya URL'si dönmedi.";
            _logger.LogError("Cloudinary yükleme hatası: {ErrorMessage}", errMsg);
            throw new InvalidOperationException($"Medya Cloudinary'ye yüklenemedi: {errMsg}");
        }

        var secureUrl = uploadResult.SecureUrl.AbsoluteUri;
        var mediaType = isVideo ? "video" : "image";
        _logger.LogInformation("Medya Cloudinary'ye yüklendi: {SecureUrl} ({MediaType}, {Bytes} byte)", secureUrl, mediaType, file.Length);
        return (secureUrl, mediaType);
    }

    public void DeleteFileIfExists(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            !relativePath.Contains("res.cloudinary.com/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (!TryExtractPublicId(relativePath, out var publicId, out var resourceType))
            {
                return;
            }

            var result = _cloudinary.Destroy(new DeletionParams(publicId)
            {
                ResourceType = resourceType
            });

            _logger.LogInformation("Cloudinary medya silme sonucu ({PublicId}): {Result}", publicId, result.Result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudinary medya silinirken hata oluştu: {Url}", relativePath);
        }
    }

    public async Task<IReadOnlyList<MediaLibraryItemDto>> ListMediaLibraryAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<MediaLibraryItemDto>();

        await AppendResourcesAsync(items, ResourceType.Image, "image", cancellationToken);
        await AppendResourcesAsync(items, ResourceType.Video, "video", cancellationToken);

        return items
            .OrderByDescending(i => i.UploadedAt)
            .ToList();
    }

    private async Task AppendResourcesAsync(
        List<MediaLibraryItemDto> target,
        ResourceType resourceType,
        string mediaType,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _cloudinary.ListResourcesAsync(new ListResourcesParams
            {
                ResourceType = resourceType,
                Type = "upload",
                MaxResults = 200,
                Direction = "desc"
            }, cancellationToken);

            if (result.Error is not null || result.Resources is null)
            {
                if (result.Error is not null)
                {
                    _logger.LogWarning("Cloudinary medya listeleme uyarısı ({ResourceType}): {Message}", resourceType, result.Error.Message);
                }
                return;
            }

            foreach (var res in result.Resources)
            {
                var url = res.SecureUrl?.AbsoluteUri ?? res.Url?.AbsoluteUri;
                if (string.IsNullOrWhiteSpace(url)) continue;

                var lastSegment = (res.PublicId ?? string.Empty).Split('/').LastOrDefault() ?? "media";
                var fileName = !string.IsNullOrWhiteSpace(res.Format) && !lastSegment.EndsWith($".{res.Format}", StringComparison.OrdinalIgnoreCase)
                    ? $"{lastSegment}.{res.Format}"
                    : lastSegment;

                var uploadedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(res.CreatedAt) &&
                    DateTime.TryParse(res.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedDate))
                {
                    uploadedAt = parsedDate;
                }

                target.Add(new MediaLibraryItemDto(
                    Url: url,
                    FileName: fileName,
                    MediaType: mediaType,
                    SizeBytes: res.Bytes,
                    UploadedAt: uploadedAt));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudinary medya kütüphanesi ({ResourceType}) listelenirken hata oluştu.", resourceType);
        }
    }

    private static bool TryExtractPublicId(string url, out string publicId, out ResourceType resourceType)
    {
        publicId = string.Empty;
        resourceType = ResourceType.Image;

        var cleanUrl = url.Split('?', '#')[0];
        var videoMarker = "/video/upload/";
        var imageMarker = "/image/upload/";

        int markerIdx;
        int markerLen;

        if ((markerIdx = cleanUrl.IndexOf(videoMarker, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            resourceType = ResourceType.Video;
            markerLen = videoMarker.Length;
        }
        else if ((markerIdx = cleanUrl.IndexOf(imageMarker, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            resourceType = ResourceType.Image;
            markerLen = imageMarker.Length;
        }
        else
        {
            return false;
        }

        var remainder = cleanUrl[(markerIdx + markerLen)..].TrimStart('/');
        // Versiyon segmentini (v123456789/) temizle
        remainder = Regex.Replace(remainder, @"^v\d+/", string.Empty);

        var dotIdx = remainder.LastIndexOf('.');
        var slashIdx = remainder.LastIndexOf('/');
        if (dotIdx > slashIdx)
        {
            remainder = remainder[..dotIdx];
        }

        publicId = Uri.UnescapeDataString(remainder);
        return !string.IsNullOrWhiteSpace(publicId);
    }
}
