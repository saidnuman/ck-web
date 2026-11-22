namespace ProductShowcase.Api.Services;

public interface IEmailSenderService
{
    Task SendPasswordResetCodeAsync(string toEmail, string fullName, string code, CancellationToken cancellationToken = default);
}
