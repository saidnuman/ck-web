using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductShowcase.Api.Data;
using ProductShowcase.Api.DTOs;
using ProductShowcase.Api.Entities;

namespace ProductShowcase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public partial class FavoritesController(ApplicationDbContext dbContext) : ControllerBase
{
    [GeneratedRegex(@"^(.+)_(slot|c|d)_(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SlotKeyRegex();

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Geçerli bir kullanıcı kimliği bulunamadı.");
        }
        return userId;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FavoriteProductDto>>> GetFavorites(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        // Yalnızca aktif, silinmemiş ve üst başlıkları dahil tamamen yayında olan ürünleri döndür
        var favorites = await dbContext.FavoriteProducts
            .AsNoTracking()
            .Where(fp =>
                fp.UserId == userId &&
                fp.Product.IsActive &&
                !fp.Product.Category.IsDeleted &&
                fp.Product.Category.IsPublished &&
                (fp.Product.Category.Parent == null || (!fp.Product.Category.Parent.IsDeleted && fp.Product.Category.Parent.IsPublished)) &&
                (fp.Product.Category.Parent == null || fp.Product.Category.Parent.Parent == null ||
                    (!fp.Product.Category.Parent.Parent.IsDeleted && fp.Product.Category.Parent.Parent.IsPublished)))
            .Include(fp => fp.Product)
                .ThenInclude(p => p.Category)
                    .ThenInclude(c => c.Parent)
                        .ThenInclude(p => p!.Parent)
            .OrderByDescending(fp => fp.CreatedAt)
            .Select(fp => new FavoriteProductDto(
                fp.ProductId,
                fp.Product.Brand,
                fp.Product.Title,
                fp.Product.Price,
                fp.Product.DiscountPercent,
                fp.Product.DiscountPercent > 0 ? $"%{fp.Product.DiscountPercent}" : "%0",
                fp.Product.ImageUrl,
                fp.Product.ProductLink,
                fp.Product.CategoryId,
                fp.Product.Category.Name,
                fp.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(favorites);
    }

    /// <summary>
    /// Kolaj slot favorilerini (Bölüm B / C / D) canlı MySQL veritabanı ile çapraz kontrol eder.
    /// Yalnızca silinmemiş, gizlenmemiş ve yayında olan sayfa/kolajlarda hâlâ mevcut görsele sahip öğeleri döner.
    /// Hayalet / eski statik / yayından kaldırılmış kayıtlar otomatik elenir.
    /// </summary>
    [HttpPost("validate")]
    public async Task<ActionResult<IEnumerable<ValidatedSlotFavoriteDto>>> ValidateSlotFavorites(
        [FromBody] ValidateFavoriteSlotsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SlotKeys is null || request.SlotKeys.Count == 0)
        {
            return Ok(Array.Empty<ValidatedSlotFavoriteDto>());
        }

        var parsedKeys = new List<(string OriginalKey, string SectionKey, int SlotNum)>();
        foreach (var rawKey in request.SlotKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var m = SlotKeyRegex().Match(rawKey.Trim());
            if (m.Success && int.TryParse(m.Groups[3].Value, out var slotNum))
            {
                parsedKeys.Add((rawKey.Trim(), m.Groups[1].Value.Trim().ToLowerInvariant(), slotNum));
            }
        }

        if (parsedKeys.Count == 0)
        {
            return Ok(Array.Empty<ValidatedSlotFavoriteDto>());
        }

        var homeSetting = await dbContext.SiteSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "default", cancellationToken);
        var homeIsPublished = homeSetting is null || homeSetting.HomeIsPublished;

        var neededSectionKeys = parsedKeys.Select(x => x.SectionKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var candidateSections = await dbContext.PageSections
            .AsNoTracking()
            .Include(s => s.Category)
                .ThenInclude(c => c!.Parent)
                    .ThenInclude(p => p!.Parent)
            .Where(s => s.IsVisible && neededSectionKeys.Contains(s.SectionKey))
            .ToListAsync(cancellationToken);

        // CategoryId null kalmış olabilecek kategori sayfaları için aktif kategorileri slug bazlı da eşleştir
        var activeCategories = await dbContext.Categories
            .AsNoTracking()
            .Include(c => c.Parent)
                .ThenInclude(p => p!.Parent)
            .Where(c => !c.IsDeleted)
            .ToListAsync(cancellationToken);
        var activeCatBySlug = activeCategories.ToDictionary(c => c.Slug, StringComparer.OrdinalIgnoreCase);

        static bool IsCategoryLive(Category? cat)
        {
            if (cat is null || cat.IsDeleted || !cat.IsPublished) return false;
            if (cat.Parent is not null && (cat.Parent.IsDeleted || !cat.Parent.IsPublished)) return false;
            if (cat.Parent?.Parent is not null && (cat.Parent.Parent.IsDeleted || !cat.Parent.Parent.IsPublished)) return false;
            return true;
        }

        var liveSectionsByKey = new Dictionary<string, PageSection>(StringComparer.OrdinalIgnoreCase);
        foreach (var sec in candidateSections)
        {
            var pk = (sec.PageKey ?? "").Trim().ToLowerInvariant();
            bool isLive;
            if (pk == "index.html")
            {
                isLive = homeIsPublished;
            }
            else
            {
                var cat = sec.Category;
                if (cat is null && !string.IsNullOrWhiteSpace(sec.CategorySlug))
                {
                    activeCatBySlug.TryGetValue(sec.CategorySlug, out cat);
                }
                isLive = IsCategoryLive(cat);
            }

            if (isLive && !liveSectionsByKey.ContainsKey(sec.SectionKey))
            {
                liveSectionsByKey[sec.SectionKey] = sec;
            }
        }

        var validResults = new List<ValidatedSlotFavoriteDto>();

        foreach (var (origKey, secKey, slotNum) in parsedKeys)
        {
            if (!liveSectionsByKey.TryGetValue(secKey, out var sec) || string.IsNullOrWhiteSpace(sec.SlotsJson))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(sec.SlotsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (!el.TryGetProperty("slot", out var slotProp) || !slotProp.TryGetInt32(out var sNum) || sNum != slotNum)
                    {
                        continue;
                    }

                    var imageUrl = el.TryGetProperty("imageUrl", out var imgProp) && imgProp.ValueKind == JsonValueKind.String
                        ? (imgProp.GetString() ?? "").Trim()
                        : "";

                    // Görseli olmayan boş/temizlenmiş slot favorilerde gösterilmez
                    if (string.IsNullOrWhiteSpace(imageUrl)) break;

                    string GetStr(string propName, string fallback = "") =>
                        el.TryGetProperty(propName, out var p) && p.ValueKind == JsonValueKind.String
                            ? (p.GetString() ?? fallback).Trim()
                            : fallback;

                    var brand = GetStr("brand", sec.Brand ?? sec.Title ?? "");
                    var title = GetStr("title", "");
                    var price = GetStr("price", "");
                    var rawDisc = GetStr("discount", "");
                    var cleanDisc = rawDisc.Replace("%", "").Trim();
                    var discountLabel = !string.IsNullOrEmpty(cleanDisc) && cleanDisc != "0" ? $"%{cleanDisc}" : "%0";
                    var linkUrl = GetStr("linkUrl", "");

                    object? mediaTransform = null;
                    if (el.TryGetProperty("mediaTransform", out var mtProp) && mtProp.ValueKind == JsonValueKind.Object)
                    {
                        mediaTransform = JsonSerializer.Deserialize<Dictionary<string, object?>>(mtProp.GetRawText());
                    }

                    validResults.Add(new ValidatedSlotFavoriteDto(
                        ProductId: origKey,
                        Brand: brand,
                        Title: title,
                        Price: price,
                        DiscountLabel: discountLabel,
                        ImageUrl: imageUrl,
                        ProductLink: string.IsNullOrWhiteSpace(linkUrl) ? "#" : linkUrl,
                        PageKey: sec.PageKey,
                        MediaTransform: mediaTransform
                    ));
                    break;
                }
            }
            catch
            {
                // Bozuk JSON slot atlanır
            }
        }

        return Ok(validResults);
    }

    [HttpDelete("{productId:int}")]
    public async Task<ActionResult<FavoriteToggleResponse>> RemoveFavorite(int productId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var favorite = await dbContext.FavoriteProducts
            .FirstOrDefaultAsync(fp => fp.UserId == userId && fp.ProductId == productId, cancellationToken);

        if (favorite is not null)
        {
            dbContext.FavoriteProducts.Remove(favorite);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new FavoriteToggleResponse(productId, false, "Ürün favorilerden çıkarıldı."));
    }

    [HttpPost("toggle/{productId:int}")]
    public async Task<ActionResult<FavoriteToggleResponse>> ToggleFavorite(int productId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var productExists = await dbContext.Products.AnyAsync(
            p => p.Id == productId && p.IsActive && !p.Category.IsDeleted && p.Category.IsPublished,
            cancellationToken);
        if (!productExists)
        {
            throw new KeyNotFoundException($"ID={productId} olan ürün bulunamadı.");
        }

        var existing = await dbContext.FavoriteProducts
            .FirstOrDefaultAsync(fp => fp.UserId == userId && fp.ProductId == productId, cancellationToken);

        if (existing is not null)
        {
            dbContext.FavoriteProducts.Remove(existing);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new FavoriteToggleResponse(productId, false, "Ürün favorilerden çıkarıldı."));
        }

        dbContext.FavoriteProducts.Add(new FavoriteProduct
        {
            UserId = userId,
            ProductId = productId,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new FavoriteToggleResponse(productId, true, "Ürün favorilere eklendi."));
    }
}
