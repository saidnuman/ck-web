using System.ComponentModel.DataAnnotations;

namespace ProductShowcase.Api.Entities;

/// <summary>
/// Site genel ayarları: Logo, üst menü/duyuru metni, footer açıklaması ve Ana Sayfa görünürlük durumu.
/// </summary>
public class SiteSetting
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Key { get; set; } = "default";

    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    [MaxLength(150)]
    public string? LogoAlt { get; set; }

    /// <summary>
    /// Header logo ölçeği (%20 – %200, varsayılan %100). Header yüksekliği sabit kalır; taşan kısım kırpılır.
    /// </summary>
    [Range(20, 200)]
    public int LogoZoomPercent { get; set; } = 100;

    [MaxLength(500)]
    public string? HeaderBannerText { get; set; }

    [MaxLength(2000)]
    public string? FooterText { get; set; }

    public bool HomeIsPublished { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
