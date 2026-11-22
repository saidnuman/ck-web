namespace ProductShowcase.Api.Services;

public interface IEmailValidationService
{
    Task<(bool IsValid, string? ErrorMessage)> ValidateEmailWithMxAsync(string? email, CancellationToken cancellationToken = default);
}
