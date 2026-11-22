namespace ProductShowcase.Api.DTOs;

public record ProductDto(
    int Id,
    string Brand,
    string Title,
    string? Description,
    decimal Price,
    int DiscountPercent,
    string DiscountLabel,
    string ImageUrl,
    string ProductLink,
    string SectionKey,
    int CategoryId,
    string CategoryName,
    string CategorySlug,
    DateTime CreatedAt
);
