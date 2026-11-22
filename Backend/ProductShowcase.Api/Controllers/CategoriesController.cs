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
public partial class CategoriesController(
    ApplicationDbContext dbContext,
    ICategoryTemplateService categoryTemplateService) : ControllerBase
{
    /// <summary>
    /// 3 seviyeli (Ana Başlık -> Alt Başlık -> Kategori) hiyerarşik ağacı döner.
    /// Canlı sitedeki Mega Menü ve Admin sol paneli tek kaynak olarak bu endpoint'i kullanır.
    /// Ziyaretçilere gizli (IsPublished = false) başlık/alt başlık/kategoriler ve onların alt elemanları kesinlikle dönülmez.
    /// </summary>
    [HttpGet("tree")]
    public async Task<ActionResult<IEnumerable<CategoryTreeNodeDto>>> GetCategoryTree(
        [FromQuery] bool includeHidden = false,
        CancellationToken cancellationToken = default)
    {
        var isAdmin = User.IsInRole("Admin");
        var showHidden = isAdmin && includeHidden;

        var allNodes = await dbContext.Categories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Slug,
                c.Description,
                c.Level,
                c.ParentId,
                c.DisplayOrder,
                c.IsPublished,
                ProductCount = c.Products.Count(p => p.IsActive)
            })
            .ToListAsync(cancellationToken);

        var filtered = showHidden
            ? allNodes
            : allNodes.Where(n => n.IsPublished).ToList();

        var lookup = filtered.ToLookup(n => n.ParentId);

        List<CategoryTreeNodeDto> BuildBranch(int? parentId, int expectedLevel)
        {
            return lookup[parentId]
                .Where(n => n.Level == expectedLevel)
                .OrderBy(n => n.DisplayOrder)
                .ThenBy(n => n.Id)
                .Select(n => new CategoryTreeNodeDto(
                    n.Id,
                    n.Name,
                    n.Slug,
                    n.Description,
                    n.Level,
                    n.ParentId,
                    n.DisplayOrder,
                    n.IsPublished,
                    n.ProductCount,
                    expectedLevel < 3 ? BuildBranch(n.Id, expectedLevel + 1) : []
                ))
                .ToList();
        }

        var tree = BuildBranch(null, 1);

        // Ebeveynsiz kalmış Level 3 kategoriler varsa Admin görebilsin
        if (showHidden)
        {
            var attachedIds = new HashSet<int>();
            void CollectIds(IEnumerable<CategoryTreeNodeDto> nodes)
            {
                foreach (var n in nodes)
                {
                    attachedIds.Add(n.Id);
                    CollectIds(n.Children);
                }
            }
            CollectIds(tree);

            var orphans = filtered
                .Where(n => n.Level == 3 && !attachedIds.Contains(n.Id))
                .Select(n => new CategoryTreeNodeDto(
                    n.Id, n.Name, n.Slug, n.Description, n.Level, n.ParentId, n.DisplayOrder, n.IsPublished, n.ProductCount, []))
                .ToList();

            if (orphans.Count > 0)
            {
                tree.Add(new CategoryTreeNodeDto(
                    -1, "DİĞER KATEGORİLER", "diger-kategoriler", null, 1, null, 9999, true, 0,
                    [new CategoryTreeNodeDto(-2, "KATEGORİSİZ", "kategorisiz", null, 2, -1, 10, true, 0, orphans)]));
            }
        }

        return Ok(tree);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories(
        [FromQuery] bool includeHidden = false,
        [FromQuery] int? level = null,
        CancellationToken cancellationToken = default)
    {
        var isAdmin = User.IsInRole("Admin");
        var query = dbContext.Categories.AsNoTracking().Where(c => !c.IsDeleted);

        if (!isAdmin || !includeHidden)
        {
            if (!isAdmin)
            {
                query = query.Where(c => c.IsPublished);
            }
        }

        if (level.HasValue)
        {
            query = query.Where(c => c.Level == level.Value);
        }

        var categories = await query
            .OrderBy(c => c.Level)
            .ThenBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id,
                c.Name,
                c.Slug,
                c.Description,
                c.Level,
                c.ParentId,
                c.DisplayOrder,
                c.Products.Count(p => p.IsActive),
                c.IsPublished))
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryDto>> CreateCategory(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var level = Math.Clamp(request.Level, 1, 3);
        int? parentId = level == 1 ? null : request.ParentId;

        if (level > 1 && parentId.HasValue)
        {
            var parent = await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == parentId.Value && !c.IsDeleted, cancellationToken);
            if (parent is null)
            {
                throw new KeyNotFoundException("Seçilen üst başlık bulunamadı.");
            }
        }

        var baseSlug = string.IsNullOrWhiteSpace(request.Slug)
            ? GenerateSlug(request.Name)
            : GenerateSlug(request.Slug);

        // Arka planda her yeni sayfaya benzersiz kimlikli slug ata (çöp kutusunda/silinmiş olsa bile aynı slug asla tekrar atanmaz)
        var maxBaseLen = Math.Min(baseSlug.Length, 100);
        var trimmedBase = baseSlug[..maxBaseLen].TrimEnd('-');
        if (string.IsNullOrWhiteSpace(trimmedBase)) trimmedBase = "sayfa";

        string slug;
        do
        {
            var uniqueToken = Guid.NewGuid().ToString("N")[..6];
            slug = $"{trimmedBase}-{uniqueToken}";
        } while (await dbContext.Categories.AnyAsync(c => c.Slug == slug, cancellationToken)
                 || await dbContext.PageSections.AnyAsync(s => s.CategorySlug == slug, cancellationToken));

        var maxOrder = await dbContext.Categories
            .Where(c => !c.IsDeleted && c.Level == level && c.ParentId == parentId)
            .Select(c => (int?)c.DisplayOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var category = new Category
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description?.Trim(),
            Level = level,
            ParentId = parentId,
            DisplayOrder = request.DisplayOrder > 0 ? request.DisplayOrder : maxOrder + 10,
            IsPublished = request.IsPublished,
            IsDeleted = false,
            TemplateInitialized = level != 3,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (level == 3)
        {
            await categoryTemplateService.EnsureCategoryTemplateAsync(category, request.TemplateMode, cancellationToken);
        }

        var dto = new CategoryDto(
            category.Id,
            category.Name,
            category.Slug,
            category.Description,
            category.Level,
            category.ParentId,
            category.DisplayOrder,
            0,
            category.IsPublished);

        return CreatedAtAction(nameof(GetCategories), new { id = category.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryDto>> UpdateCategory(
        int id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            throw new KeyNotFoundException($"ID={id} olan öğe bulunamadı.");
        }

        var oldSlug = category.Slug;
        category.Name = request.Name.Trim();
        if (request.Description is not null) category.Description = request.Description.Trim();
        if (request.DisplayOrder.HasValue) category.DisplayOrder = request.DisplayOrder.Value;
        if (request.IsPublished.HasValue) category.IsPublished = request.IsPublished.Value;
        if (request.ParentId.HasValue && category.Level > 1) category.ParentId = request.ParentId.Value;

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var newSlug = GenerateSlug(request.Slug);
            if (!string.Equals(newSlug, oldSlug, StringComparison.OrdinalIgnoreCase))
            {
                var duplicate = await dbContext.Categories.AnyAsync(c => c.Slug == newSlug && c.Id != id, cancellationToken);
                if (duplicate)
                {
                    throw new InvalidOperationException($"'{newSlug}' slug değeri başka bir öğe tarafından kullanılıyor.");
                }
                category.Slug = newSlug;

                if (category.Level == 3)
                {
                    var oldPageKey = $"index.html?kategori={oldSlug}";
                    var legacyOldPageKey = $"ek_1.html?kategori={oldSlug}";
                    var newPageKey = $"index.html?kategori={newSlug}";
                    var sections = await dbContext.PageSections
                        .Where(s => s.CategoryId == category.Id || s.CategorySlug == oldSlug || s.PageKey == oldPageKey || s.PageKey == legacyOldPageKey)
                        .ToListAsync(cancellationToken);
                    foreach (var sec in sections)
                    {
                        sec.CategorySlug = newSlug;
                        sec.PageKey = newPageKey;
                    }
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new CategoryDto(
            category.Id,
            category.Name,
            category.Slug,
            category.Description,
            category.Level,
            category.ParentId,
            category.DisplayOrder,
            category.Products.Count(p => p.IsActive),
            category.IsPublished));
    }

    [HttpPost("reorder")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ReorderCategories(
        [FromBody] ReorderCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OrderedIds is null || request.OrderedIds.Count == 0)
        {
            return BadRequest(new { message = "Sıralama listesi boş olamaz." });
        }

        var nodes = await dbContext.Categories
            .Where(c => request.OrderedIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        var byId = nodes.ToDictionary(n => n.Id);
        int order = 10;
        foreach (var id in request.OrderedIds)
        {
            if (byId.TryGetValue(id, out var node))
            {
                node.DisplayOrder = order;
                order += 10;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { updated = nodes.Count });
    }

    [HttpPatch("{id:int}/visibility")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryDto>> UpdateCategoryVisibility(
        int id,
        [FromBody] UpdateCategoryVisibilityRequest request,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            throw new KeyNotFoundException($"ID={id} olan öğe bulunamadı.");
        }

        category.IsPublished = request.IsPublished;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new CategoryDto(
            category.Id,
            category.Name,
            category.Slug,
            category.Description,
            category.Level,
            category.ParentId,
            category.DisplayOrder,
            category.Products.Count(p => p.IsActive),
            category.IsPublished));
    }

    [HttpPatch("by-slug/{slug}/visibility")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryDto>> UpdateCategoryVisibilityBySlug(
        string slug,
        [FromBody] UpdateCategoryVisibilityRequest request,
        [FromQuery] string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        var category = await categoryTemplateService.EnsureCategoryAndTemplateBySlugAsync(
            slug, displayName, "ek1", cancellationToken);

        category.IsPublished = request.IsPublished;
        await dbContext.SaveChangesAsync(cancellationToken);

        var productCount = await dbContext.Products.CountAsync(p => p.CategoryId == category.Id && p.IsActive, cancellationToken);
        return Ok(new CategoryDto(
            category.Id,
            category.Name,
            category.Slug,
            category.Description,
            category.Level,
            category.ParentId,
            category.DisplayOrder,
            productCount,
            category.IsPublished));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken cancellationToken)
    {
        var all = await dbContext.Categories
            .ToListAsync(cancellationToken);

        var root = all.FirstOrDefault(c => c.Id == id && !c.IsDeleted);
        if (root is null)
        {
            throw new KeyNotFoundException($"ID={id} olan öğe bulunamadı.");
        }

        var byId = all.ToDictionary(c => c.Id);

        string BuildParentPath(Category node)
        {
            var parts = new List<string>();
            var cur = node.ParentId.HasValue && byId.TryGetValue(node.ParentId.Value, out var p) ? p : null;
            var guard = 0;
            while (cur is not null && guard++ < 5)
            {
                parts.Insert(0, cur.Name);
                cur = cur.ParentId.HasValue && byId.TryGetValue(cur.ParentId.Value, out var gp) ? gp : null;
            }
            return parts.Count > 0 ? string.Join(" › ", parts) : "Kök Menü";
        }

        // Soft Delete: Kök düğümü ve tüm alt düğümlerini Çöp Kutusuna taşı (kolajlar ve ürünler korunur)
        var now = DateTime.UtcNow;
        var toSoftDelete = new List<Category>();
        void CollectSubtree(Category node)
        {
            foreach (var child in all.Where(c => c.ParentId == node.Id && !c.IsDeleted))
            {
                CollectSubtree(child);
            }
            toSoftDelete.Add(node);
        }
        CollectSubtree(root);

        foreach (var node in toSoftDelete)
        {
            node.IsDeleted = true;
            node.DeletedAt = now;
            node.DeletedFromPath = BuildParentPath(node);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // =========================================================================
    // ÇÖP KUTUSU (SOFT DELETE YÖNETİMİ): LİSTELE / GERİ GETİR / KALICI SİL / BOŞALT
    // =========================================================================

    [HttpGet("trash")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<TrashCategoryDto>>> GetTrashCategories(CancellationToken cancellationToken)
    {
        var trashed = await dbContext.Categories
            .AsNoTracking()
            .Where(c => c.IsDeleted)
            .OrderByDescending(c => c.DeletedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => new TrashCategoryDto(
                c.Id,
                c.Name,
                c.Slug,
                c.Level,
                c.ParentId,
                c.DeletedFromPath,
                c.DeletedAt,
                c.PageSections.Count,
                c.Products.Count(p => p.IsActive)))
            .ToListAsync(cancellationToken);

        return Ok(trashed);
    }

    [HttpPost("trash/{id:int}/restore")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryDto>> RestoreTrashCategory(int id, CancellationToken cancellationToken)
    {
        var all = await dbContext.Categories.ToListAsync(cancellationToken);
        var target = all.FirstOrDefault(c => c.Id == id && c.IsDeleted);
        if (target is null)
        {
            throw new KeyNotFoundException($"Çöp kutusunda ID={id} olan kayıt bulunamadı.");
        }

        var byId = all.ToDictionary(c => c.Id);

        // Üst başlık zinciri çöp kutusundaysa onları da geri getir; üst başlık kalıcı silindiyse uygun aktif ebeveyne bağla
        var toRestore = new List<Category>();
        var ancestor = target.ParentId.HasValue && byId.TryGetValue(target.ParentId.Value, out var pNode) ? pNode : null;
        var guard = 0;
        while (ancestor is not null && guard++ < 5)
        {
            if (ancestor.IsDeleted)
            {
                toRestore.Insert(0, ancestor);
            }
            ancestor = ancestor.ParentId.HasValue && byId.TryGetValue(ancestor.ParentId.Value, out var gpNode) ? gpNode : null;
        }

        if (target.Level > 1 && (!target.ParentId.HasValue || !byId.ContainsKey(target.ParentId.Value)))
        {
            var fallbackParent = all
                .Where(c => !c.IsDeleted && c.Level == target.Level - 1)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            target.ParentId = fallbackParent?.Id;
        }

        // Hedefin altındaki çöp kutusundaki alt düğümleri de topla
        void CollectDeletedDescendants(Category node)
        {
            if (!toRestore.Contains(node)) toRestore.Add(node);
            foreach (var child in all.Where(c => c.ParentId == node.Id && c.IsDeleted))
            {
                CollectDeletedDescendants(child);
            }
        }
        CollectDeletedDescendants(target);

        // Çakışma kontrolü: Aktif panelde aynı isimde öğe varsa otomatik numara ata (Örn: "Çanta (2)", "canta-2")
        var activeNamesByLevel = all
            .Where(c => !c.IsDeleted)
            .GroupBy(c => c.Level)
            .ToDictionary(
                g => g.Key,
                g => new HashSet<string>(g.Select(x => x.Name.Trim()), StringComparer.OrdinalIgnoreCase));

        var allSlugs = new HashSet<string>(all.Select(c => c.Slug), StringComparer.OrdinalIgnoreCase);

        foreach (var node in toRestore)
        {
            if (!activeNamesByLevel.TryGetValue(node.Level, out var usedNames))
            {
                usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                activeNamesByLevel[node.Level] = usedNames;
            }

            var origName = node.Name.Trim();
            var origSlug = node.Slug;

            if (usedNames.Contains(origName))
            {
                var cleanBaseName = Regex.Replace(origName, @"\s*\(\d+\)$", "").Trim();
                if (string.IsNullOrWhiteSpace(cleanBaseName)) cleanBaseName = origName;
                var num = 2;
                string nextName;
                do
                {
                    nextName = $"{cleanBaseName} ({num++})";
                } while (usedNames.Contains(nextName));

                node.Name = nextName;
                usedNames.Add(nextName);

                // Slug'ı da çakışmasız numaralı hâle getir (Örn: canta-2) ve PageSections/PageSnapshots ile senkronla
                var cleanBaseSlug = GenerateSlug(cleanBaseName);
                var slugNum = num - 1;
                string nextSlug = $"{cleanBaseSlug}-{slugNum}";
                while (allSlugs.Contains(nextSlug))
                {
                    slugNum++;
                    nextSlug = $"{cleanBaseSlug}-{slugNum}";
                }

                allSlugs.Remove(origSlug);
                allSlugs.Add(nextSlug);
                node.Slug = nextSlug;

                if (node.Level == 3)
                {
                    var oldPageKey = $"index.html?kategori={origSlug}";
                    var legacyOldPageKey = $"ek_1.html?kategori={origSlug}";
                    var newPageKey = $"index.html?kategori={nextSlug}";

                    var secList = await dbContext.PageSections
                        .Where(s => s.CategoryId == node.Id || s.CategorySlug == origSlug || s.PageKey == oldPageKey || s.PageKey == legacyOldPageKey)
                        .ToListAsync(cancellationToken);
                    foreach (var sec in secList)
                    {
                        sec.CategoryId = node.Id;
                        sec.CategorySlug = nextSlug;
                        sec.PageKey = newPageKey;
                    }

                    var snapList = await dbContext.PageSnapshots
                        .Where(s => s.CategoryId == node.Id || s.CategorySlug == origSlug || s.PageKey == oldPageKey || s.PageKey == legacyOldPageKey)
                        .ToListAsync(cancellationToken);
                    foreach (var snap in snapList)
                    {
                        snap.CategoryId = node.Id;
                        snap.CategorySlug = nextSlug;
                        snap.PageKey = newPageKey;
                        snap.PageTitle = nextName;
                    }
                }
            }
            else
            {
                usedNames.Add(origName);
            }

            node.IsDeleted = false;
            node.DeletedAt = null;
            node.DeletedFromPath = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var prodCount = await dbContext.Products.CountAsync(p => p.CategoryId == target.Id && p.IsActive, cancellationToken);
        return Ok(new CategoryDto(
            target.Id,
            target.Name,
            target.Slug,
            target.Description,
            target.Level,
            target.ParentId,
            target.DisplayOrder,
            prodCount,
            target.IsPublished));
    }

    [HttpDelete("trash/{id:int}/permanent")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PermanentDeleteTrashCategory(int id, CancellationToken cancellationToken)
    {
        var all = await dbContext.Categories
            .Include(c => c.Products)
            .Include(c => c.PageSections)
            .ToListAsync(cancellationToken);

        var root = all.FirstOrDefault(c => c.Id == id && c.IsDeleted);
        if (root is null)
        {
            throw new KeyNotFoundException($"Çöp kutusunda ID={id} olan kayıt bulunamadı.");
        }

        var toDelete = new List<Category>();
        void CollectSubtree(Category node)
        {
            foreach (var child in all.Where(c => c.ParentId == node.Id))
            {
                CollectSubtree(child);
            }
            toDelete.Add(node);
        }
        CollectSubtree(root);

        var delIds = toDelete.Select(c => c.Id).ToHashSet();
        var delSlugs = toDelete.Select(c => c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var snapshots = await dbContext.PageSnapshots
            .Where(s => (s.CategoryId.HasValue && delIds.Contains(s.CategoryId.Value)) ||
                        (s.CategorySlug != null && delSlugs.Contains(s.CategorySlug)))
            .ToListAsync(cancellationToken);
        if (snapshots.Count > 0) dbContext.PageSnapshots.RemoveRange(snapshots);

        foreach (var node in toDelete)
        {
            if (node.Products.Count > 0) dbContext.Products.RemoveRange(node.Products);
            if (node.PageSections.Count > 0) dbContext.PageSections.RemoveRange(node.PageSections);
        }

        dbContext.Categories.RemoveRange(toDelete);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("trash/empty")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> EmptyTrash(CancellationToken cancellationToken)
    {
        var trashed = await dbContext.Categories
            .Include(c => c.Products)
            .Include(c => c.PageSections)
            .Where(c => c.IsDeleted)
            .ToListAsync(cancellationToken);

        if (trashed.Count == 0)
        {
            return Ok(new { deleted = 0 });
        }

        var delIds = trashed.Select(c => c.Id).ToHashSet();
        var delSlugs = trashed.Select(c => c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var snapshots = await dbContext.PageSnapshots
            .Where(s => (s.CategoryId.HasValue && delIds.Contains(s.CategoryId.Value)) ||
                        (s.CategorySlug != null && delSlugs.Contains(s.CategorySlug)))
            .ToListAsync(cancellationToken);
        if (snapshots.Count > 0) dbContext.PageSnapshots.RemoveRange(snapshots);

        foreach (var node in trashed)
        {
            if (node.Products.Count > 0) dbContext.Products.RemoveRange(node.Products);
            if (node.PageSections.Count > 0) dbContext.PageSections.RemoveRange(node.PageSections);
        }

        dbContext.Categories.RemoveRange(trashed);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { deleted = trashed.Count });
    }

    private static string GenerateSlug(string input)
    {
        var normalized = input.Trim().ToLowerInvariant()
            .Replace("ç", "c").Replace("ğ", "g").Replace("ı", "i")
            .Replace("ö", "o").Replace("ş", "s").Replace("ü", "u");
        normalized = SlugRegex().Replace(normalized, "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? $"kategori-{Guid.NewGuid():N}"[..16] : normalized;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
