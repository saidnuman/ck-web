using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductShowcase.Api.Entities;

/// <summary>
/// Admin "KAYDET / YAYINLA" butonuna bastığında ilgili sayfanın o anki tam kolaj/içerik durumunun JSON yedeğini saklar.
/// Sayfa başına son 20 sürüm korunur.
/// </summary>
public class PageSnapshot
{
    public int Id { get; set; }

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
    [Column(TypeName = "longtext")]
    public string SnapshotJson { get; set; } = "[]";

    [MaxLength(250)]
    public string? Summary { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
