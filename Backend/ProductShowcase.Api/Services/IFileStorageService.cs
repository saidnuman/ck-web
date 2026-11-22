namespace ProductShowcase.Api.Services;

public interface IFileStorageService
{
    Task<(string RelativeUrl, string MediaType)> SaveMediaFileAsync(IFormFile file, CancellationToken cancellationToken = default);
    void DeleteFileIfExists(string? relativePath);
}
