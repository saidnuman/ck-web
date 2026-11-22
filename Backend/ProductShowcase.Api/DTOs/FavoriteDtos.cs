namespace ProductShowcase.Api.DTOs;

public record FavoriteProductDto(
    int ProductId,
    string Brand,
    string Title,
    decimal Price,
    int DiscountPercent,
    string DiscountLabel,
    string ImageUrl,
    string ProductLink,
    int CategoryId,
    string CategoryName,
    DateTime AddedAt
);

public record FavoriteToggleResponse(
    int ProductId,
    bool IsFavorited,
    string Message
);

public record ValidateFavoriteSlotsRequest(
    List<string> SlotKeys
);

public record ValidatedSlotFavoriteDto(
    string ProductId,
    string Brand,
    string Title,
    string Price,
    string DiscountLabel,
    string ImageUrl,
    string ProductLink,
    string PageKey,
    object? MediaTransform
);
