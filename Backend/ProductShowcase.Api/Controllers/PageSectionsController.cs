using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductShowcase.Api.Data;
using ProductShowcase.Api.DTOs;
using ProductShowcase.Api.Entities;
using ProductShowcase.Api.Services;

namespace ProductShowcase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class PageSectionsController(
    ApplicationDbContext dbContext,
    IFileStorageService fileStorageService,
    ICategoryTemplateService categoryTemplateService,
    ILogger<PageSectionsController> logger) : ControllerBase
{
    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ExtractSlugFromPageKey(string? pageKey)
    {
        if (string.IsNullOrWhiteSpace(pageKey)) return null;
        var match = KategoriSlugRegex().Match(pageKey);
        if (!match.Success) return null;
        return Uri.UnescapeDataString(match.Groups[1].Value).Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"[?&]kategori=([^&#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex KategoriSlugRegex();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PageSectionDto>>> GetPageSections(
        [FromQuery] string? pageKey = null,
        [FromQuery] string? categorySlug = null,
        [FromQuery] string? categoryName = null,
        CancellationToken cancellationToken = default)
    {
        var isAdmin = User.IsInRole("Admin");
        var resolvedSlug = NullIfEmpty(categorySlug)?.ToLowerInvariant() ?? ExtractSlugFromPageKey(pageKey);

        if (!string.IsNullOrWhiteSpace(resolvedSlug))
        {
            var existingCat = await dbContext.Categories
                .AsNoTracking()
                .Include(c => c.Parent)
                    .ThenInclude(p => p!.Parent)
                .FirstOrDefaultAsync(c => c.Slug == resolvedSlug, cancellationToken);

            // Soft-delete (Çöp Kutusu) kontrolü: Silinmiş bir kategori veya üst başlığı silinmişse canlı sitede asla görünmez / yeniden oluşturulmaz
            var isDeletedBySelfOrAncestor = existingCat is not null && (
                existingCat.IsDeleted ||
                (existingCat.Parent is not null && existingCat.Parent.IsDeleted) ||
                (existingCat.Parent?.Parent is not null && existingCat.Parent.Parent.IsDeleted)
            );

            if (isDeletedBySelfOrAncestor)
            {
                return StatusCode(StatusCodes.Status404NotFound, new
                {
                    message = "Bu sayfa silinmiş (çöp kutusunda) veya artık mevcut değil.",
                    isDeleted = true,
                    categorySlug = resolvedSlug
                });
            }

            // Gizli (Taslak) sayfa kontrolü: Kendisi VEYA üst başlık/alt başlıklarından biri gizliyse ziyaretçi erişemez
            var isHiddenBySelfOrAncestor = existingCat is not null && (
                !existingCat.IsPublished ||
                (existingCat.Parent is not null && !existingCat.Parent.IsPublished) ||
                (existingCat.Parent?.Parent is not null && !existingCat.Parent.Parent.IsPublished)
            );

            if (isHiddenBySelfOrAncestor && !isAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Bu sayfa şu anda yapım aşamasında (gizli) modundadır.",
                    isHidden = true,
                    categorySlug = resolvedSlug
                });
            }

            // Kategori mevcut değilse veya henüz şablonu oluşturulmadıysa otomatik ek_1 başlangıç şablonunu klonla
            var category = await categoryTemplateService.EnsureCategoryAndTemplateBySlugAsync(
                resolvedSlug,
                categoryName,
                "ek1",
                cancellationToken);

            var canonicalPageKey = $"index.html?kategori={category.Slug}";
            var legacyPageKey = $"ek_1.html?kategori={category.Slug}";
            var catQuery = dbContext.PageSections
                .AsNoTracking()
                .Where(s => s.CategoryId == category.Id || s.CategorySlug == category.Slug || s.PageKey == canonicalPageKey || s.PageKey == legacyPageKey);

            if (!isAdmin)
            {
                catQuery = catQuery.Where(s => s.IsVisible);
            }

            var categorySections = await catQuery
                .OrderBy(s => s.DisplayOrder)
                .ThenBy(s => s.Id)
                .Select(s => MapToDto(s))
                .ToListAsync(cancellationToken);

            return Ok(categorySections);
        }

        var query = dbContext.PageSections.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(pageKey))
        {
            var normalizedPage = pageKey.Trim().ToLowerInvariant();
            query = query.Where(s => s.PageKey == normalizedPage);
        }

        if (!isAdmin)
        {
            query = query.Where(s => s.IsVisible);
        }

        var sections = await query
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Id)
            .Select(s => MapToDto(s))
            .ToListAsync(cancellationToken);

        return Ok(sections);
    }

    /// <summary>
    /// Fotoğraf (.jpg, .png, .webp, .gif) veya video (.mp4, .webm, .mov) dosyasını
    /// wwwroot/uploads/ klasörüne benzersiz Guid adıyla kaydeder.
    /// </summary>
    [HttpPost("upload-media")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(105 * 1024 * 1024)]
    public async Task<ActionResult<UploadedImageResponse>> UploadSectionMedia(
        IFormFile image,
        CancellationToken cancellationToken)
    {
        var (relativeUrl, mediaType) = await fileStorageService.SaveMediaFileAsync(image, cancellationToken);
        return Ok(new UploadedImageResponse(relativeUrl, mediaType));
    }

    /// <summary>
    /// Aktif medya deposundaki (Cloudinary veya yerel wwwroot/uploads/) ve veritabanında kayıtlı
    /// harici (https://) fotoğraf/video dosyalarını en yeniden eskiye listeler.
    /// Admin Paneli Medya Kütüphanesi modalı tarafından kullanılır.
    /// </summary>
    [HttpGet("media-library")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<MediaLibraryItemDto>>> GetMediaLibrary(CancellationToken cancellationToken)
    {
        var storageItems = await fileStorageService.ListMediaLibraryAsync(cancellationToken);
        var items = new List<MediaLibraryItemDto>(storageItems);
        var seenUrls = new HashSet<string>(
            items.Select(i => i.Url),
            StringComparer.OrdinalIgnoreCase);

        void TryAddExternalDbUrl(string? rawUrl, DateTime createdAt)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return;
            var trimmed = rawUrl.Trim();
            if (!trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!seenUrls.Add(trimmed)) return;

            var cleanNoQuery = trimmed.Split('?', '#')[0];
            var ext = Path.GetExtension(cleanNoQuery);
            var isVideo = cleanNoQuery.Contains("/video/upload/", StringComparison.OrdinalIgnoreCase) ||
                          ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                          ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
                          ext.Equals(".mov", StringComparison.OrdinalIgnoreCase);
            var fileName = Path.GetFileName(cleanNoQuery);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = isVideo ? "video.mp4" : "image.jpg";

            items.Add(new MediaLibraryItemDto(
                Url: trimmed,
                FileName: Uri.UnescapeDataString(fileName),
                MediaType: isVideo ? "video" : "image",
                SizeBytes: 0,
                UploadedAt: createdAt));
        }

        void CollectUrlsFromJson(System.Text.Json.JsonElement element, DateTime createdAt)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String &&
                        (prop.Name.Equals("imageUrl", StringComparison.OrdinalIgnoreCase) ||
                         prop.Name.Equals("videoUrl", StringComparison.OrdinalIgnoreCase)))
                    {
                        TryAddExternalDbUrl(prop.Value.GetString(), createdAt);
                    }
                    else if (prop.Value.ValueKind is System.Text.Json.JsonValueKind.Array or System.Text.Json.JsonValueKind.Object)
                    {
                        CollectUrlsFromJson(prop.Value, createdAt);
                    }
                }
            }
            else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    CollectUrlsFromJson(child, createdAt);
                }
            }
        }

        // Veritabanında kayıtlı harici (Cloudinary/HTTPS) medya URL'lerini de kütüphaneye tamamlayıcı olarak ekle
        var sectionMedia = await dbContext.PageSections
            .AsNoTracking()
            .Select(s => new { s.ImageUrl, s.SlotsJson, s.CreatedAt })
            .ToListAsync(cancellationToken);

        foreach (var sec in sectionMedia)
        {
            TryAddExternalDbUrl(sec.ImageUrl, sec.CreatedAt);
            if (!string.IsNullOrWhiteSpace(sec.SlotsJson))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(sec.SlotsJson);
                    CollectUrlsFromJson(doc.RootElement, sec.CreatedAt);
                }
                catch { /* geçersiz JSON yoksay */ }
            }
        }

        var productMedia = await dbContext.Products
            .AsNoTracking()
            .Where(p => !string.IsNullOrEmpty(p.ImageUrl))
            .Select(p => new { p.ImageUrl, p.CreatedAt })
            .ToListAsync(cancellationToken);

        foreach (var prod in productMedia)
        {
            TryAddExternalDbUrl(prod.ImageUrl, prod.CreatedAt);
        }

        var siteLogos = await dbContext.SiteSettings
            .AsNoTracking()
            .Where(s => !string.IsNullOrEmpty(s.LogoUrl))
            .Select(s => new { s.LogoUrl, s.UpdatedAt })
            .ToListAsync(cancellationToken);

        foreach (var logo in siteLogos)
        {
            TryAddExternalDbUrl(logo.LogoUrl, logo.UpdatedAt);
        }

        return Ok(items.OrderByDescending(i => i.UploadedAt).ToList());
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(105 * 1024 * 1024)]
    public async Task<ActionResult<PageSectionDto>> UpsertPageSection(
        [FromForm] UpsertPageSectionRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedPage = request.PageKey.Trim().ToLowerInvariant();
        var normalizedTemplate = request.TemplateType.Trim().ToLowerInvariant();
        var resolvedSlug = NullIfEmpty(request.CategorySlug)?.ToLowerInvariant()
            ?? ExtractSlugFromPageKey(normalizedPage);

        int? resolvedCategoryId = request.CategoryId;
        if (!string.IsNullOrWhiteSpace(resolvedSlug))
        {
            var category = await categoryTemplateService.EnsureCategoryAndTemplateBySlugAsync(resolvedSlug, null, "ek1", cancellationToken);
            resolvedCategoryId = category.Id;
            resolvedSlug = category.Slug;
            normalizedPage = $"index.html?kategori={category.Slug}";
        }

        var sectionKey = string.IsNullOrWhiteSpace(request.SectionKey)
            ? $"{normalizedTemplate.Replace('-', '_')}_u{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..36]
            : request.SectionKey.Trim().ToLowerInvariant();

        string? uploadedImageUrl = null;
        if (request.Image is not null && request.Image.Length > 0)
        {
            var (url, _) = await fileStorageService.SaveMediaFileAsync(request.Image, cancellationToken);
            uploadedImageUrl = url;
        }

        try
        {
            var existing = await dbContext.PageSections
                .FirstOrDefaultAsync(s => s.PageKey == normalizedPage && s.SectionKey == sectionKey, cancellationToken);

            if (existing is not null)
            {
                var oldImage = existing.ImageUrl;
                existing.CategoryId = resolvedCategoryId ?? existing.CategoryId;
                existing.CategorySlug = resolvedSlug ?? existing.CategorySlug;
                existing.Brand = NullIfEmpty(request.Brand);
                existing.Title = NullIfEmpty(request.Title);
                existing.Subtitle = NullIfEmpty(request.Subtitle);
                existing.Price = request.Price;
                existing.DiscountPercent = request.DiscountPercent;
                existing.TemplateType = normalizedTemplate;
                existing.InsertAfterSectionKey = NullIfEmpty(request.InsertAfterSectionKey)?.ToLowerInvariant();
                existing.IsVisible = request.IsVisible;
                existing.DisplayOrder = request.DisplayOrder;
                if (request.SlotsJson is not null)
                {
                    var trimmedSlots = request.SlotsJson.Trim();
                    existing.SlotsJson = (trimmedSlots == "" || trimmedSlots == "[]") ? null : trimmedSlots;
                    if (uploadedImageUrl is null)
                    {
                        existing.ImageUrl = ExtractSlot1ImageUrl(existing.SlotsJson);
                    }
                }
                if (uploadedImageUrl is not null)
                {
                    existing.ImageUrl = uploadedImageUrl;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                if (uploadedImageUrl is not null && oldImage is not null)
                {
                    fileStorageService.DeleteFileIfExists(oldImage);
                }

                logger.LogInformation("Sayfa bölümü güncellendi: {PageKey} / {SectionKey}", existing.PageKey, existing.SectionKey);
                return Ok(MapToDto(existing));
            }

            var cleanedSlots = request.SlotsJson?.Trim();
            var normalizedSlots = (string.IsNullOrWhiteSpace(cleanedSlots) || cleanedSlots == "[]") ? null : cleanedSlots;
            var finalImageUrl = uploadedImageUrl ?? ExtractSlot1ImageUrl(normalizedSlots);

            var entity = new PageSection
            {
                PageKey = normalizedPage,
                CategoryId = resolvedCategoryId,
                CategorySlug = resolvedSlug,
                SectionKey = sectionKey,
                TemplateType = normalizedTemplate,
                InsertAfterSectionKey = NullIfEmpty(request.InsertAfterSectionKey)?.ToLowerInvariant(),
                IsCustom = request.IsCustom,
                IsVisible = request.IsVisible,
                Brand = NullIfEmpty(request.Brand),
                Title = NullIfEmpty(request.Title),
                Subtitle = NullIfEmpty(request.Subtitle),
                Price = request.Price,
                DiscountPercent = request.DiscountPercent,
                ImageUrl = finalImageUrl,
                SlotsJson = normalizedSlots,
                DisplayOrder = request.DisplayOrder,
                CreatedAt = DateTime.UtcNow
            };

            dbContext.PageSections.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Yeni sayfa bölümü eklendi: {PageKey} / {SectionKey}", entity.PageKey, entity.SectionKey);
            return CreatedAtAction(nameof(GetPageSections), new { pageKey = entity.PageKey }, MapToDto(entity));
        }
        catch
        {
            if (uploadedImageUrl is not null)
            {
                fileStorageService.DeleteFileIfExists(uploadedImageUrl);
            }
            throw;
        }
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(105 * 1024 * 1024)]
    public async Task<ActionResult<PageSectionDto>> UpdatePageSection(
        int id,
        [FromForm] UpsertPageSectionRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.PageSections.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null)
        {
            throw new KeyNotFoundException($"ID={id} olan sayfa bölümü bulunamadı.");
        }

        string? uploadedImageUrl = null;
        string? oldImage = null;

        if (request.Image is not null && request.Image.Length > 0)
        {
            var (url, _) = await fileStorageService.SaveMediaFileAsync(request.Image, cancellationToken);
            uploadedImageUrl = url;
            oldImage = entity.ImageUrl;
        }

        try
        {
            // Kategori bağlantısı eksik kalmış eski kayıtları da kanonik PageKey / CategoryId / CategorySlug ile senkronla
            var normalizedPage = NullIfEmpty(request.PageKey)?.ToLowerInvariant();
            var resolvedSlug = NullIfEmpty(request.CategorySlug)?.ToLowerInvariant()
                ?? ExtractSlugFromPageKey(normalizedPage)
                ?? ExtractSlugFromPageKey(entity.PageKey);

            if (!string.IsNullOrWhiteSpace(resolvedSlug))
            {
                var category = await categoryTemplateService.EnsureCategoryAndTemplateBySlugAsync(resolvedSlug, null, "ek1", cancellationToken);
                entity.CategoryId = category.Id;
                entity.CategorySlug = category.Slug;
                entity.PageKey = $"index.html?kategori={category.Slug}";
            }
            else if (!string.IsNullOrWhiteSpace(normalizedPage))
            {
                entity.PageKey = normalizedPage;
            }

            entity.Brand = NullIfEmpty(request.Brand);
            entity.Title = NullIfEmpty(request.Title);
            entity.Subtitle = NullIfEmpty(request.Subtitle);
            entity.Price = request.Price;
            entity.DiscountPercent = request.DiscountPercent;
            entity.TemplateType = request.TemplateType.Trim().ToLowerInvariant();
            entity.InsertAfterSectionKey = NullIfEmpty(request.InsertAfterSectionKey)?.ToLowerInvariant();
            entity.IsVisible = request.IsVisible;
            entity.DisplayOrder = request.DisplayOrder;
            if (request.SlotsJson is not null)
            {
                var trimmedSlots = request.SlotsJson.Trim();
                entity.SlotsJson = (trimmedSlots == "" || trimmedSlots == "[]") ? null : trimmedSlots;
                if (uploadedImageUrl is null)
                {
                    entity.ImageUrl = ExtractSlot1ImageUrl(entity.SlotsJson);
                }
            }
            if (uploadedImageUrl is not null)
            {
                entity.ImageUrl = uploadedImageUrl;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (oldImage is not null)
            {
                fileStorageService.DeleteFileIfExists(oldImage);
            }

            return Ok(MapToDto(entity));
        }
        catch
        {
            if (uploadedImageUrl is not null)
            {
                fileStorageService.DeleteFileIfExists(uploadedImageUrl);
            }
            throw;
        }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeletePageSection(int id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.PageSections.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null)
        {
            throw new KeyNotFoundException($"ID={id} olan sayfa bölümü bulunamadı.");
        }

        var imageUrl = entity.ImageUrl;
        dbContext.PageSections.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        fileStorageService.DeleteFileIfExists(imageUrl);
        logger.LogInformation("Sayfa bölümü silindi: {Id} ({SectionKey})", id, entity.SectionKey);
        return NoContent();
    }

    private static string? ExtractSlot1ImageUrl(string? slotsJson)
    {
        if (string.IsNullOrWhiteSpace(slotsJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(slotsJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("slot", out var slotProp) && slotProp.TryGetInt32(out var slotNum) && slotNum == 1)
                {
                    if (item.TryGetProperty("imageUrl", out var urlProp) && urlProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var val = urlProp.GetString();
                        return string.IsNullOrWhiteSpace(val) ? null : val;
                    }
                    return null;
                }
            }
        }
        catch
        {
            // Geçersiz JSON varsa mevcut görsel null olarak değerlendirilir.
        }
        return null;
    }

    private static PageSectionDto MapToDto(PageSection s) => new(
        s.Id,
        s.PageKey,
        s.CategoryId,
        s.CategorySlug,
        s.SectionKey,
        s.TemplateType,
        s.InsertAfterSectionKey,
        s.IsCustom,
        s.IsVisible,
        s.Brand,
        s.Title,
        s.Subtitle,
        s.Price,
        s.DiscountPercent,
        s.ImageUrl,
        s.SlotsJson,
        s.DisplayOrder,
        s.CreatedAt
    );
}
