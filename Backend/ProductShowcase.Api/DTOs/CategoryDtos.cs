using System.ComponentModel.DataAnnotations;

namespace ProductShowcase.Api.DTOs;

public record CategoryDto(
    int Id,
    string Name,
    string Slug,
    string? Description,
    int Level,
    int? ParentId,
    int DisplayOrder,
    int ProductCount,
    bool IsPublished = true
);

public record CategoryTreeNodeDto(
    int Id,
    string Name,
    string Slug,
    string? Description,
    int Level,
    int? ParentId,
    int DisplayOrder,
    bool IsPublished,
    int ProductCount,
    List<CategoryTreeNodeDto> Children
);

public record CreateCategoryRequest(
    [Required(ErrorMessage = "Ad alanı zorunludur.")]
    [MaxLength(100)]
    string Name,

    [MaxLength(120)]
    string? Slug = null,

    [MaxLength(500)]
    string? Description = null,

    int Level = 3,

    int? ParentId = null,

    int DisplayOrder = 0,

    /// <summary>
    /// Yalnızca Level 3 kategoriler için başlangıç şablonu: "ek1", "home", "empty"
    /// </summary>
    [MaxLength(20)]
    string TemplateMode = "ek1",

    bool IsPublished = true
);

public record UpdateCategoryRequest(
    [Required(ErrorMessage = "Ad alanı zorunludur.")]
    [MaxLength(100)]
    string Name,

    [MaxLength(120)]
    string? Slug = null,

    [MaxLength(500)]
    string? Description = null,

    int? ParentId = null,

    int? DisplayOrder = null,

    bool? IsPublished = null
);

public record UpdateCategoryVisibilityRequest(bool IsPublished);

public record ReorderCategoriesRequest(
    [Required]
    List<int> OrderedIds
);

public record TrashCategoryDto(
    int Id,
    string Name,
    string Slug,
    int Level,
    int? ParentId,
    string? DeletedFromPath,
    DateTime? DeletedAt,
    int SectionCount,
    int ProductCount
);
