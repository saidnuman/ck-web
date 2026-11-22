using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductShowcase.Api.Entities;

public class PageSection
{
    public int Id { get; set; }

    /// <summary>
    /// Hangi sayfaya ait olduğu (ör. index.html veya ek_1.html?kategori=canta)
    /// </summary>
    [Required]
    [MaxLength(120)]
    public string PageKey { get; set; } = "index.html";

    /// <summary>
    /// Kategori izolasyonu için Foreign Key (Ana Sayfa bölümlerinde null olabilir)
    /// </summary>
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>
    /// Kategori izolasyonu için URL-dostu slug (ör. canta, patik, yatak-ortusu)
    /// </summary>
    [MaxLength(120)]
    public string? CategorySlug { get; set; }

    /// <summary>
    /// Sayfa/kategori içindeki çakışmasız benzersiz bölüm kimliği (ör. canta_bolum_c_3 veya bolum_c_u17320391_1)
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string SectionKey { get; set; } = string.Empty;

    /// <summary>
    /// Bölüm şablonu (bolum-a, bolum-b, bolum-c, bolum-d, video, video-hero)
    /// </summary>
    [Required]
    [MaxLength(40)]
    public string TemplateType { get; set; } = "bolum-c";

    /// <summary>
    /// Yeni eklenen bölümün hangi mevcut bölümden hemen sonra yerleşeceğini belirtir
    /// </summary>
    [MaxLength(100)]
    public string? InsertAfterSectionKey { get; set; }

    /// <summary>
    /// true: Düzenlenebilir/silinebilir dinamik veya klonlanmış kategori bölümü; false: index.html yerleşik güncellemesi
    /// </summary>
    public bool IsCustom { get; set; } = true;

    [MaxLength(100)]
    public string? Brand { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? Subtitle { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Price { get; set; }

    [Range(0, 100)]
    public int? DiscountPercent { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Kolaj/slider/video slotlarının medya yolları ve satır içi düzenlenen metin verileri (JSON)
    /// </summary>
    [Column(TypeName = "longtext")]
    public string? SlotsJson { get; set; }

    /// <summary>
    /// true: Ziyaretçilere görünür; false: Admin panelinde göz ikonu ile geçici olarak gizlenmiş.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    public int DisplayOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
