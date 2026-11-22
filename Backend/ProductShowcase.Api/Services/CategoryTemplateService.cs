using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductShowcase.Api.Data;
using ProductShowcase.Api.Entities;

namespace ProductShowcase.Api.Services;

/// <summary>
/// Yalnızca Seviye 3 (Kategori) sayfaları için seçilen başlangıç şablonunu ("ek1", "home", "empty")
/// kategoriye özel benzersiz ID'lerle ve tamamen boş medya alanlarıyla (null) MySQL veritabanına klonlar.
/// Video bölümleri orijinal varsayılan yazılar ve 3 sekme (Instagram, TikTok, YouTube) ile hazır açılır.
/// </summary>
public class CategoryTemplateService(
    ApplicationDbContext dbContext,
    ILogger<CategoryTemplateService> logger) : ICategoryTemplateService
{
    public static string BuildDefaultVideoCarouselSlotsJson()
    {
        var defaultVideoTabs = new
        {
            mode = "video-carousel-tabs",
            seeMoreText = "Tümünü Gör",
            seeMoreLink = "",
            tabs = new object[]
            {
                new
                {
                    id = "instagram",
                    name = "Instagram",
                    items = new object[]
                    {
                        new { id = "ig_1", badge = "• Canlı", title = "Yaz Koleksiyonu", subtitle = "Kamera Arkası Görüntüleri", videoUrl = (string?)null, mediaType = "video", linkUrl = (string?)null },
                        new { id = "ig_2", badge = "• Reel", title = "Yeni Ürünler Geldi", subtitle = "Hemen İncele", videoUrl = (string?)null, mediaType = "video", linkUrl = (string?)null },
                        new { id = "ig_3", badge = "• Stil", title = "Kombin Önerileri", subtitle = "Sezonun Favorileri", videoUrl = (string?)null, mediaType = "video", linkUrl = (string?)null }
                    }
                },
                new
                {
                    id = "tiktok",
                    name = "TikTok",
                    items = new object[]
                    {
                        new { id = "tt_1", badge = "• Trend", title = "Günlük Kombin", subtitle = "Sokak Modası", videoUrl = (string?)null, mediaType = "video", linkUrl = (string?)null },
                        new { id = "tt_2", badge = "• Viral", title = "Bunu Denedin mi?", subtitle = "Yeni Akım", videoUrl = (string?)null, mediaType = "video", linkUrl = (string?)null }
                    }
                },
                new
                {
                    id = "youtube",
                    name = "YouTube",
                    items = new object[]
                    {
                        new { id = "yt_1", badge = "• Vlog", title = "Moda Haftası", subtitle = "Bölüm 1", videoUrl = (string?)null, mediaType = "video", linkUrl = (string?)null },
                        new { id = "yt_2", badge = "• Kutu Açılımı", title = "Yeni Sezon Parçalar", subtitle = "Gelin Birlikte Açalım", videoUrl = (string?)null, mediaType = "video", linkUrl = (string?)null }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(defaultVideoTabs);
    }

    public static string BuildDefaultHeroVideoSlotsJson()
    {
        var defaultSlides = new object[]
        {
            new { slot = 1, imageUrl = (string?)null, mediaType = "video", title = "NEW ESSENTIALS", description = "Hareketli detaylar, yeni sezonun favorileri.", buttonText = "Shop Now", linkUrl = (string?)null },
            new { slot = 2, imageUrl = (string?)null, mediaType = "video", title = "SIGNATURE COLLECTION", description = "Her ana uyum sağlayan mükemmel tasarımlar.", buttonText = "Shop Now", linkUrl = (string?)null },
            new { slot = 3, imageUrl = (string?)null, mediaType = "video", title = "YENİ SEZON", description = "Sınırları zorlayan yepyeni bir koleksiyon.", buttonText = "Shop Now", linkUrl = (string?)null }
        };
        return JsonSerializer.Serialize(defaultSlides);
    }

    private static readonly (string KeySuffix, string TemplateType, string? Title, string? Subtitle, int DisplayOrder)[] Ek1StarterTemplate =
    [
        ("bolum_a_1",  "bolum-a", "ÖNE ÇIKAN KOLEKSİYON", "Sezonun en trend parçalarını keşfedin.", 10),
        ("bolum_b_2",  "bolum-b", "Öne Çıkan Koleksiyonlar", null, 20),
        ("bolum_c_3",  "bolum-c", null, null, 30),
        ("bolum_b_4",  "bolum-b", "Sezon Seçkisi", null, 40),
        ("bolum_c_5",  "bolum-c", null, null, 50),
        ("bolum_c_6",  "bolum-c", null, null, 60),
        ("video_7",    "video",   "Şu An Trend Olanlar", null, 70)
    ];

    private static readonly (string KeySuffix, string TemplateType, string? Title, string? Subtitle, int DisplayOrder)[] HomeStarterTemplate =
    [
        ("hero_1",     "video-hero", "NEW ESSENTIALS", "Hareketli detaylar, yeni sezonun favorileri.", 10),
        ("bolum_d_2",  "bolum-d",    "ÖNE ÇIKANLAR", null, 20),
        ("bolum_d_3",  "bolum-d",    "YENİ GELENLER", null, 30),
        ("bolum_b_4",  "bolum-b",    "Özel Seçki", null, 40),
        ("video_5",    "video",      "Şu An Trend Olanlar", null, 50)
    ];

    public async Task<Category> EnsureCategoryAndTemplateBySlugAsync(
        string slug,
        string? displayName = null,
        string templateMode = "ek1",
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var category = await dbContext.Categories
            .FirstOrDefaultAsync(c => c.Slug == normalizedSlug && !c.IsDeleted, cancellationToken);

        if (category is null)
        {
            var friendlyName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName.Trim()
                : FormatSlugAsName(normalizedSlug);

            // Eğer aynı slug çöp kutusunda (IsDeleted = true) ise asla geri getirme; yeni benzersiz slug üret
            var slugTaken = await dbContext.Categories.AnyAsync(c => c.Slug == normalizedSlug, cancellationToken);
            var finalSlug = slugTaken
                ? $"{normalizedSlug}-{Guid.NewGuid().ToString("N")[..6]}"
                : normalizedSlug;

            // Varsayılan olarak ilk aktif Level 2 alt başlığın altına bağla (varsa)
            var fallbackSubHeading = await dbContext.Categories
                .Where(c => c.Level == 2 && !c.IsDeleted)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            category = new Category
            {
                Name = friendlyName,
                Slug = finalSlug,
                Description = $"{friendlyName} kategorisi",
                Level = 3,
                ParentId = fallbackSubHeading?.Id,
                IsPublished = true,
                IsDeleted = false,
                TemplateInitialized = false,
                CreatedAt = DateTime.UtcNow
            };

            dbContext.Categories.Add(category);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Dinamik kategori veritabanında oluşturuldu: {Name} ({Slug})", category.Name, category.Slug);
        }

        if (category.Level == 3)
        {
            await EnsureCategoryTemplateAsync(category, templateMode, cancellationToken);
        }
        return category;
    }

    public async Task EnsureCategoryTemplateAsync(
        Category category,
        string templateMode = "ek1",
        CancellationToken cancellationToken = default)
    {
        // Yalnızca Level 3 (Kategori) sayfaları için kolaj şablonu klonlanır
        if (category.Level != 3 || category.TemplateInitialized)
        {
            return;
        }

        var mode = (templateMode ?? "ek1").Trim().ToLowerInvariant();
        var pageKey = $"index.html?kategori={category.Slug}";
        var legacyPageKey = $"ek_1.html?kategori={category.Slug}";
        var hasExistingSections = await dbContext.PageSections
            .AnyAsync(s => s.CategoryId == category.Id || s.CategorySlug == category.Slug || s.PageKey == pageKey || s.PageKey == legacyPageKey, cancellationToken);

        if (!hasExistingSections && mode != "empty")
        {
            var selectedTemplate = mode == "home" ? HomeStarterTemplate : Ek1StarterTemplate;
            var safePrefix = category.Slug.Replace('-', '_');
            var clonedSections = selectedTemplate.Select(tpl => new PageSection
            {
                PageKey = pageKey,
                CategoryId = category.Id,
                CategorySlug = category.Slug,
                SectionKey = $"{safePrefix}_{tpl.KeySuffix}",
                TemplateType = tpl.TemplateType,
                InsertAfterSectionKey = null,
                IsCustom = true,
                IsVisible = true,
                Brand = null,
                Title = tpl.Title,
                Subtitle = tpl.Subtitle,
                Price = null,
                DiscountPercent = null,
                ImageUrl = null,
                SlotsJson = tpl.TemplateType == "video"
                    ? BuildDefaultVideoCarouselSlotsJson()
                    : tpl.TemplateType == "video-hero"
                        ? BuildDefaultHeroVideoSlotsJson()
                        : null,
                DisplayOrder = tpl.DisplayOrder,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            await dbContext.PageSections.AddRangeAsync(clonedSections, cancellationToken);
            logger.LogInformation("'{Category}' ({Slug}) kategorisi için '{Mode}' şablonundan {Count} adet boş bölüm klonlandı.",
                category.Name, category.Slug, mode, clonedSections.Count);
        }

        category.TemplateInitialized = true;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task InitializeAllExistingCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var uninitialized = await dbContext.Categories
            .Where(c => c.Level == 3 && !c.IsDeleted && !c.TemplateInitialized)
            .ToListAsync(cancellationToken);

        foreach (var cat in uninitialized)
        {
            await EnsureCategoryTemplateAsync(cat, "ek1", cancellationToken);
        }
    }

    private static string FormatSlugAsName(string slug)
    {
        var parts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
