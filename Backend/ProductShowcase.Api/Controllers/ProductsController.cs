using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductShowcase.Api.Data;
using ProductShowcase.Api.DTOs;
using ProductShowcase.Api.Entities;

namespace ProductShowcase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(ApplicationDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Tüm ürünleri bağlı oldukları kategori bilgileriyle (Include) birlikte listeler.
    /// İsteğe bağlı olarak categoryId, categorySlug, sectionKey veya search ile filtrelenebilir.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(
        [FromQuery] int? categoryId,
        [FromQuery] string? categorySlug,
        [FromQuery] string? sectionKey,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        IQueryable<Product> query = dbContext.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive);

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(categorySlug))
        {
            var normalizedSlug = categorySlug.Trim().ToLowerInvariant();
            query = query.Where(p => p.Category.Slug == normalizedSlug);
        }

        if (!string.IsNullOrWhiteSpace(sectionKey))
        {
            var normalizedSection = sectionKey.Trim().ToLowerInvariant();
            query = query.Where(p => p.SectionKey == normalizedSection);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                p.Title.ToLower().Contains(term) ||
                p.Brand.ToLower().Contains(term) ||
                p.Category.Name.ToLower().Contains(term));
        }

        var products = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Select(p => MapToDto(p))
            .ToListAsync(cancellationToken);

        return Ok(products);
    }

    private static ProductDto MapToDto(Product p) => new(
        p.Id,
        p.Brand,
        p.Title,
        p.Description,
        p.Price,
        p.DiscountPercent,
        p.DiscountPercent > 0 ? $"%{p.DiscountPercent}" : "%0",
        p.ImageUrl,
        p.ProductLink,
        p.SectionKey,
        p.CategoryId,
        p.Category?.Name ?? string.Empty,
        p.Category?.Slug ?? string.Empty,
        p.CreatedAt
    );
}
