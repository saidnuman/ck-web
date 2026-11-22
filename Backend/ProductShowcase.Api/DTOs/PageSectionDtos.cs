using System.ComponentModel.DataAnnotations;

namespace ProductShowcase.Api.DTOs;

public record PageSectionDto(
    int Id,
    string PageKey,
    int? CategoryId,
    string? CategorySlug,
    string SectionKey,
    string TemplateType,
    string? InsertAfterSectionKey,
    bool IsCustom,
    bool IsVisible,
    string? Brand,
    string? Title,
    string? Subtitle,
    decimal? Price,
    int? DiscountPercent,
    string? ImageUrl,
    string? SlotsJson,
    int DisplayOrder,
    DateTime CreatedAt
);

public class UpsertPageSectionRequest
{
    [Required(ErrorMessage = "Sayfa anahtarı (PageKey) zorunludur.")]
    [MaxLength(120)]
    public string PageKey { get; set; } = "index.html";

    public int? CategoryId { get; set; }

    [MaxLength(120)]
    public string? CategorySlug { get; set; }

    [MaxLength(100)]
    public string? SectionKey { get; set; }

    [Required(ErrorMessage = "Bölüm şablonu (TemplateType) zorunludur.")]
    [MaxLength(40)]
    public string TemplateType { get; set; } = "bolum-c";

    [MaxLength(100)]
    public string? InsertAfterSectionKey { get; set; }

    public bool IsCustom { get; set; } = true;

    public bool IsVisible { get; set; } = true;

    [MaxLength(100)]
    public string? Brand { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? Subtitle { get; set; }

    public decimal? Price { get; set; }

    [Range(0, 100)]
    public int? DiscountPercent { get; set; }

    public string? SlotsJson { get; set; }

    public int DisplayOrder { get; set; } = 0;

    public IFormFile? Image { get; set; }
}

public record UploadedImageResponse(string ImageUrl, string MediaType = "image");

public record MediaLibraryItemDto(
    string Url,
    string FileName,
    string MediaType,
    long SizeBytes,
    DateTime UploadedAt
);
