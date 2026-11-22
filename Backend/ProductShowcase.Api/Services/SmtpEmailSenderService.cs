using System.Net;
using System.Net.Mail;

namespace ProductShowcase.Api.Services;

/// <summary>
/// appsettings.Local.json veya ortam değişkenlerindeki Smtp:* ayarlarıyla gerçek SMTP üzerinden e-posta gönderir.
/// SMTP henüz yapılandırılmamışsa sunucu çökmeden açıklayıcı hata mesajı döner.
/// </summary>
public class SmtpEmailSenderService(
    IConfiguration configuration,
    ILogger<SmtpEmailSenderService> logger) : IEmailSenderService
{
    private static bool IsMissingOrPlaceholder(string? val) =>
        string.IsNullOrWhiteSpace(val) ||
        val.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase) ||
        val.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

    public async Task SendPasswordResetCodeAsync(string toEmail, string fullName, string code, CancellationToken cancellationToken = default)
    {
        var host = configuration["Smtp:Host"]?.Trim();
        var portStr = configuration["Smtp:Port"]?.Trim();
        var smtpEmail = configuration["Smtp:Email"]?.Trim();
        var smtpPassword = configuration["Smtp:Password"];
        var fromName = configuration["Smtp:FromName"]?.Trim();
        if (string.IsNullOrWhiteSpace(fromName)) fromName = "SKIMS Destek";

        if (IsMissingOrPlaceholder(host) || IsMissingOrPlaceholder(smtpEmail) || IsMissingOrPlaceholder(smtpPassword))
        {
            throw new InvalidOperationException(
                "E-posta gönderim servisi (SMTP) henüz yapılandırılmamış. Lütfen appsettings.Local.json dosyasındaki Smtp ayarlarını tamamlayın.");
        }

        var port = int.TryParse(portStr, out var parsedPort) && parsedPort > 0 ? parsedPort : 587;

        using var message = new MailMessage
        {
            From = new MailAddress(smtpEmail!, fromName),
            Subject = "Şifre Sıfırlama Doğrulama Kodunuz",
            IsBodyHtml = true,
            Body = $"""
                <div style="font-family:Arial,Helvetica,sans-serif; max-width:520px; margin:0 auto; padding:24px; border:1px solid #eaeaea; background:#ffffff; color:#111;">
                    <h2 style="margin:0 0 12px 0; font-size:20px;">Şifre Sıfırlama Kodu</h2>
                    <p style="font-size:14px; line-height:1.6; color:#333;">Merhaba <strong>{WebUtility.HtmlEncode(fullName)}</strong>,</p>
                    <p style="font-size:14px; line-height:1.6; color:#333;">Şifrenizi sıfırlamak için aşağıdaki 6 haneli tek kullanımlık doğrulama kodunu kullanın:</p>
                    <div style="margin:22px 0; padding:16px; text-align:center; background:#f6f5f2; border:1px dashed #ccc; font-size:30px; font-weight:800; letter-spacing:8px; color:#111;">
                        {WebUtility.HtmlEncode(code)}
                    </div>
                    <p style="font-size:12.5px; color:#666; line-height:1.5;">Bu kod <strong>15 dakika</strong> boyunca geçerlidir ve yalnızca bir kez kullanılabilir. Bu talebi siz oluşturmadıysanız bu e-postayı görmezden gelebilirsiniz.</p>
                </div>
                """
        };
        message.To.Add(new MailAddress(toEmail));

        using var client = new SmtpClient(host!, port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(smtpEmail, smtpPassword),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };

        try
        {
            await client.SendMailAsync(message, cancellationToken);
            logger.LogInformation("Şifre sıfırlama doğrulama kodu e-posta ile gönderildi: {ToEmail}", toEmail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMTP e-posta gönderimi başarısız oldu: {ToEmail}", toEmail);
            throw new InvalidOperationException("Doğrulama kodu e-posta ile gönderilemedi. SMTP bağlantı bilgilerinizi (e-posta / uygulama şifresi) kontrol edin.");
        }
    }
}
