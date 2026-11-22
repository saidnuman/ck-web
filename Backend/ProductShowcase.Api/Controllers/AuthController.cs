using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductShowcase.Api.Data;
using ProductShowcase.Api.DTOs;
using ProductShowcase.Api.Entities;
using ProductShowcase.Api.Services;

namespace ProductShowcase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    ApplicationDbContext dbContext,
    IJwtTokenService jwtTokenService,
    PasswordHasher<User> passwordHasher,
    IEmailValidationService emailValidationService,
    IEmailSenderService emailSenderService,
    IConfiguration configuration,
    ILogger<AuthController> logger) : ControllerBase
{
    private static bool IsMissingOrPlaceholder(string? val) =>
        string.IsNullOrWhiteSpace(val) ||
        val.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase) ||
        val.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

    private static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var (isEmailValid, emailError) = await emailValidationService.ValidateEmailWithMxAsync(request.Email, cancellationToken);
        if (!isEmailValid)
        {
            throw new ArgumentException(emailError ?? "Lütfen geçerli ve aktif bir e-posta adresi girin.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var exists = await dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("Bu e-posta adresi ile kayıtlı bir hesap zaten mevcut.");
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = normalizedEmail,
            Role = "User",
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        var (token, expiresAt) = jwtTokenService.GenerateToken(user);
        logger.LogInformation("Yeni kullanıcı kayıt oldu: {Email}", user.Email);

        return Ok(new AuthResponse(
            token,
            expiresAt,
            new AuthUserDto(user.Id, user.FullName, user.Email, user.Role)));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            return Unauthorized(new { message = "E-posta veya şifre hatalı." });
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "E-posta veya şifre hatalı." });
        }

        var (token, expiresAt) = jwtTokenService.GenerateToken(user);
        logger.LogInformation("Kullanıcı giriş yaptı: {Email} ({Role})", user.Email, user.Role);

        return Ok(new AuthResponse(
            token,
            expiresAt,
            new AuthUserDto(user.Id, user.FullName, user.Email, user.Role)));
    }

    /// <summary>
    /// Frontend Google Identity Services (GIS) için yalnızca herkese açık Google Client ID bilgisini döner.
    /// </summary>
    [HttpGet("google-client-id")]
    public ActionResult<GoogleClientIdResponse> GetGoogleClientId()
    {
        var clientId = configuration["Google:ClientId"]?.Trim() ?? "";
        var enabled = !IsMissingOrPlaceholder(clientId);
        return Ok(new GoogleClientIdResponse(enabled ? clientId : "", enabled));
    }

    /// <summary>
    /// Google Identity Services'ten gelen idToken'ı GoogleJsonWebSignature.ValidateAsync ile doğrular;
    /// kullanıcı veritabanında varsa giriş yaptırır, yoksa otomatik yeni hesap oluşturup JWT döner.
    /// </summary>
    [HttpPost("google")]
    public async Task<ActionResult<AuthResponse>> GoogleLoginOrRegister(
        [FromBody] GoogleAuthRequest request,
        CancellationToken cancellationToken)
    {
        var googleClientId = configuration["Google:ClientId"]?.Trim();
        if (IsMissingOrPlaceholder(googleClientId))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Google ile giriş henüz yapılandırılmamış. Lütfen appsettings.Local.json içinde Google:ClientId değerini tanımlayın."
            });
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [googleClientId!]
            };
            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning(ex, "Geçersiz Google idToken doğrulama denemesi.");
            return Unauthorized(new { message = "Google oturumu doğrulanamadı veya süresi doldu." });
        }

        if (string.IsNullOrWhiteSpace(payload.Email) || !payload.EmailVerified)
        {
            return Unauthorized(new { message = "Google hesabınızın e-posta adresi doğrulanmamış." });
        }

        var normalizedEmail = payload.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            var displayName = !string.IsNullOrWhiteSpace(payload.Name)
                ? payload.Name.Trim()
                : normalizedEmail.Split('@')[0];
            if (displayName.Length > 120) displayName = displayName[..120];

            user = new User
            {
                FullName = displayName,
                Email = normalizedEmail,
                Role = "User",
                CreatedAt = DateTime.UtcNow
            };
            // Google ile açılan hesap için tahmin edilemez kriptografik rastgele şifre özeti ata
            var randomSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            user.PasswordHash = passwordHasher.HashPassword(user, randomSecret);

            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Google ile yeni kullanıcı kaydı oluşturuldu: {Email}", user.Email);
        }
        else
        {
            logger.LogInformation("Google ile kullanıcı giriş yaptı: {Email} ({Role})", user.Email, user.Role);
        }

        var (token, expiresAt) = jwtTokenService.GenerateToken(user);
        return Ok(new AuthResponse(
            token,
            expiresAt,
            new AuthUserDto(user.Id, user.FullName, user.Email, user.Role)));
    }

    /// <summary>
    /// Şifremi Unuttum (Adım 1): Kriptografik 6 haneli tek kullanımlık doğrulama kodu üretip MySQL'e kaydeder ve SMTP ile gönderir.
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            return NotFound(new { message = "Bu e-posta adresiyle kayıtlı bir hesap bulunamadı." });
        }

        // Kriptografik olarak güvenli 6 haneli sayısal kod üret (100000 - 999999)
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        var codeHash = ComputeSha256Hex($"{normalizedEmail}:{code}");

        // Önceki kullanılmamış kodları geçersiz kıl
        var oldCodes = await dbContext.PasswordResetCodes
            .Where(r => r.UserId == user.Id && !r.IsUsed)
            .ToListAsync(cancellationToken);
        foreach (var old in oldCodes)
        {
            old.IsUsed = true;
        }

        var resetEntity = new PasswordResetCode
        {
            UserId = user.Id,
            Email = normalizedEmail,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            IsUsed = false,
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.PasswordResetCodes.Add(resetEntity);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSenderService.SendPasswordResetCodeAsync(user.Email, user.FullName, code, cancellationToken);
        }
        catch
        {
            // E-posta gönderilemediyse oluşturulan kodu geçersiz kıl
            resetEntity.IsUsed = true;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        return Ok(new
        {
            message = "6 haneli doğrulama kodu e-posta adresinize gönderildi. Kod 15 dakika geçerlidir."
        });
    }

    /// <summary>
    /// Şifremi Unuttum (Adım 2 & 3): 6 haneli kodu ve süresini doğrular, yeni şifreyi hash'leyip MySQL'de günceller ve kodu tek kullanımlık olarak kapatır.
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.NewPassword, request.ConfirmNewPassword, StringComparison.Ordinal))
        {
            throw new ArgumentException("Girilen şifreler birbiriyle uyuşmuyor.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            throw new ArgumentException("Doğrulama kodu geçersiz veya süresi dolmuş.");
        }

        var now = DateTime.UtcNow;
        var activeReset = await dbContext.PasswordResetCodes
            .Where(r => r.UserId == user.Id && !r.IsUsed && r.ExpiresAt > now)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeReset is null)
        {
            throw new ArgumentException("Doğrulama kodunun süresi dolmuş veya geçersiz. Lütfen yeni kod isteyin.");
        }

        if (activeReset.AttemptCount >= 5)
        {
            activeReset.IsUsed = true;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new ArgumentException("Çok fazla hatalı deneme yapıldı. Lütfen yeni bir doğrulama kodu isteyin.");
        }

        var expectedHashBytes = Encoding.UTF8.GetBytes(activeReset.CodeHash);
        var providedHashBytes = Encoding.UTF8.GetBytes(ComputeSha256Hex($"{normalizedEmail}:{request.Code.Trim()}"));
        var isMatch = expectedHashBytes.Length == providedHashBytes.Length &&
                      CryptographicOperations.FixedTimeEquals(expectedHashBytes, providedHashBytes);

        if (!isMatch)
        {
            activeReset.AttemptCount++;
            if (activeReset.AttemptCount >= 5) activeReset.IsUsed = true;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new ArgumentException("Doğrulama kodu hatalı. Lütfen e-postanızdaki 6 haneli kodu kontrol edin.");
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        activeReset.IsUsed = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Kullanıcı şifresi doğrulama koduyla güncellendi: {Email}", user.Email);
        return Ok(new
        {
            message = "Şifreniz başarıyla güncellendi. Yeni şifrenizle giriş yapabilirsiniz."
        });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Geçersiz oturum bilgisi." });
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Unauthorized(new { message = "Kullanıcı bulunamadı." });
        }

        return Ok(new AuthUserDto(user.Id, user.FullName, user.Email, user.Role));
    }
}
