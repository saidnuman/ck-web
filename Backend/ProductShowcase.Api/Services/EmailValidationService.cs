using System.Text.RegularExpressions;
using DnsClient;
using DnsClient.Protocol;

namespace ProductShowcase.Api.Services;

/// <summary>
/// Sıkı e-posta format kontrolü + alan adının gerçek DNS MX (Mail Exchange) kaydı doğrulaması.
/// Uydurma / e-posta almayan alan adlarını (ör. test@asdasd123.xyz) reddeder.
/// </summary>
public partial class EmailValidationService(ILogger<EmailValidationService> logger) : IEmailValidationService
{
    private const string InvalidEmailMessage = "Lütfen geçerli ve aktif bir e-posta adresi girin.";

    // Sıkı e-posta biçimi: yerel kısım + @ + en az iki etiketli geçerli alan adı ve harflerden oluşan TLD (min 2 karakter)
    [GeneratedRegex(@"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]{1,64}@(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,24}$", RegexOptions.Compiled)]
    private static partial Regex StrictEmailRegex();

    private readonly LookupClient _dnsClient = new(new LookupClientOptions
    {
        Timeout = TimeSpan.FromSeconds(4),
        Retries = 1,
        UseCache = true
    });

    public async Task<(bool IsValid, string? ErrorMessage)> ValidateEmailWithMxAsync(string? email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, InvalidEmailMessage);
        }

        var trimmed = email.Trim();
        if (trimmed.Length > 200 || trimmed.Contains("..") || !StrictEmailRegex().IsMatch(trimmed))
        {
            return (false, InvalidEmailMessage);
        }

        var atIndex = trimmed.LastIndexOf('@');
        if (atIndex <= 0 || atIndex >= trimmed.Length - 1)
        {
            return (false, InvalidEmailMessage);
        }

        var domain = trimmed[(atIndex + 1)..].ToLowerInvariant();
        if (domain.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
            domain == "localhost")
        {
            return (false, InvalidEmailMessage);
        }

        try
        {
            var result = await _dnsClient.QueryAsync(domain, QueryType.MX, cancellationToken: cancellationToken);
            if (result.HasError)
            {
                logger.LogInformation("E-posta MX sorgusu başarısız ({Domain}): {Error}", domain, result.ErrorMessage);
                return (false, InvalidEmailMessage);
            }

            var hasValidMx = result.Answers
                .OfType<MxRecord>()
                .Any(mx => !string.IsNullOrWhiteSpace(mx.Exchange?.Value) && mx.Exchange.Value.Trim() != ".");

            if (!hasValidMx)
            {
                logger.LogInformation("E-posta alan adında geçerli MX kaydı bulunamadı: {Domain}", domain);
                return (false, InvalidEmailMessage);
            }

            return (true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DNS MX doğrulaması sırasında hata oluştu ({Domain})", domain);
            return (false, InvalidEmailMessage);
        }
    }
}
