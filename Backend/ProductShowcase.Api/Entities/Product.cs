using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductShowcase.Api.Entities;

public class Product
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Brand { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Range(0, 100)]
    public int DiscountPercent { get; set; } = 0;

    /// <summary>
    /// Sunucu tarafında wwwroot/uploads/ klasörüne yüklenen görselin otomatik yolu (ör. /uploads/guid.jpg)
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ProductLink { get; set; } = "#";

    /// <summary>
    /// Ön yüzdeki hangi kolaj bölümünde yer alacağını belirtir (ör. bolum-d, bolum-d2, ek1-bolum-c)
    /// </summary>
    [MaxLength(60)]
    public string SectionKey { get; set; } = "bolum-d";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // One-to-Many: Her ürünün bir kategorisi vardır
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    // Many-to-Many: Ürün birden fazla kullanıcı tarafından favorilenebilir
    public ICollection<FavoriteProduct> FavoriteProducts { get; set; } = new List<FavoriteProduct>();
}
