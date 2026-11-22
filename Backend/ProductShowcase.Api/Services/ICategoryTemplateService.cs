using ProductShowcase.Api.Entities;

namespace ProductShowcase.Api.Services;

public interface ICategoryTemplateService
{
    Task<Category> EnsureCategoryAndTemplateBySlugAsync(
        string slug,
        string? displayName = null,
        string templateMode = "ek1",
        CancellationToken cancellationToken = default);

    Task EnsureCategoryTemplateAsync(
        Category category,
        string templateMode = "ek1",
        CancellationToken cancellationToken = default);

    Task InitializeAllExistingCategoriesAsync(CancellationToken cancellationToken = default);
}
