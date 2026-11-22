using System.ComponentModel.DataAnnotations;

namespace ProductShowcase.Api.DTOs;

public record PageSnapshotSummaryDto(
    int Id,
    string PageKey,
    string PageTitle,
    int? CategoryId,
    string? CategorySlug,
    bool IsPublished,
    string? Summary,
    DateTime CreatedAt
);

public record PageSnapshotDetailDto(
    int Id,
    string PageKey,
    string PageTitle,
    int? CategoryId,
    string? CategorySlug,
    bool IsPublished,
    string SnapshotJson,
    string? Summary,
    DateTime CreatedAt
);

public class CreatePageSnapshotRequest
{
    [Required]
    [MaxLength(120)]
    public string PageKey { get; set; } = "index.html";

    [Required]
    [MaxLength(150)]
    public string PageTitle { get; set; } = "Ana Sayfa";

    public int? CategoryId { get; set; }

    [MaxLength(120)]
    public string? CategorySlug { get; set; }

    public bool IsPublished { get; set; } = true;

    [Required]
    public string SnapshotJson { get; set; } = "[]";

    [MaxLength(250)]
    public string? Summary { get; set; }
}

public record SiteSettingDto(
    string? LogoUrl,
    int LogoZoomPercent,
    string? HeaderBannerText,
    string? FooterText,
    bool HomeIsPublished,
    DateTime UpdatedAt
);

public class UpdateSiteSettingRequest
{
    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    [Range(20, 200, ErrorMessage = "Logo boyutu %20 ile %200 arasında olmalıdır.")]
    public int? LogoZoomPercent { get; set; }

    [MaxLength(500)]
    public string? HeaderBannerText { get; set; }

    [MaxLength(2000)]
    public string? FooterText { get; set; }

    public bool? HomeIsPublished { get; set; }

    public IFormFile? LogoFile { get; set; }
}
