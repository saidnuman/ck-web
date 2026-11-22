using System.ComponentModel.DataAnnotations;

namespace ProductShowcase.Api.Entities;

/// <summary>
/// 3 Seviyeli Hiyerarşik Menü ve Sayfa Yapısı:
/// - Level 1: Ana Başlık (Klasör — ör. EV VE YAŞAM)
/// - Level 2: Alt Başlık (Klasör — ör. YATAK VE SALON)
/// - Level 3: Kategori (Gerçek dinamik sayfa — ek_1.html?kategori=&lt;slug&gt;)
/// </summary>
public class Category
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 1 = Ana Başlık, 2 = Alt Başlık, 3 = Kategori (Sayfa)
    /// </summary>
    public int Level { get; set; } = 3;

    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();

    public int DisplayOrder { get; set; } = 0;

    /// <summary>
    /// Tüm seviyelerde (Ana Başlık, Alt Başlık, Kategori) çalışır.
    /// false olduğunda normal ziyaretçilere ve canlı Mega Menüye görünmez.
    /// </summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>
    /// Yalnızca Level 3 kategoriler için varsayılan başlangıç şablonunun klonlanıp klonlanmadığını belirtir.
    /// </summary>
    public bool TemplateInitialized { get; set; } = false;

    /// <summary>
    /// Soft Delete (Çöp Kutusu): true olduğunda canlı sitede ve aktif admin ağacında görünmez; Çöp Kutusundan geri getirilebilir.
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    [MaxLength(300)]
    public string? DeletedFromPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<PageSection> PageSections { get; set; } = new List<PageSection>();
}
