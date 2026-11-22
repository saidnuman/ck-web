using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductShowcase.Api.Data;
using ProductShowcase.Api.DTOs;
using ProductShowcase.Api.Entities;
using ProductShowcase.Api.Services;

namespace ProductShowcase.Api.Controllers;

/// <summary>
/// Genel Site Ayarları > Yetkili Admin Yönetimi API'si.
/// Mevcut adminler ile normal kullanıcıları kesin olarak ayırır, kendi hesabının yetkisini kaldırmayı
/// engeller ve tüm rol/hesap değişikliklerinde mevcut aktif adminin şifresini veritabanındaki hash ile doğrular.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminUsersController(
    ApplicationDbContext dbContext,
    PasswordHasher<User> passwordHasher,
    IEmailValidationService emailValidationService,
    ILogger<AdminUsersController> logger) : ControllerBase
{
    private int GetCurrentAdminUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(claim, out var userId))
        {
            throw new UnauthorizedAccessException("Geçerli bir admin oturumu bulunamadı.");
        }
        return userId;
    }

    private async Task<User> VerifyCurrentAdminPasswordAsync(string? currentAdminPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentAdminPassword))
        {
            throw new ArgumentException("İşlemi onaylamak için mevcut admin şifrenizi girmeniz zorunludur.");
        }

        var currentAdminId = GetCurrentAdminUserId();
        var currentAdmin = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == currentAdminId && u.Role == "Admin", cancellationToken);

        if (currentAdmin is null)
        {
            throw new UnauthorizedAccessException("Aktif admin hesabı doğrulanamadı.");
        }

        var verification = passwordHasher.VerifyHashedPassword(currentAdmin, currentAdmin.PasswordHash, currentAdminPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw new ArgumentException("Mevcut admin şifreniz hatalı. İşlem güvenlik nedeniyle iptal edildi.");
        }

        return currentAdmin;
    }

    /// <summary>
    /// Yalnızca şu an Admin rolüne sahip olan hesapları listeler.
    /// </summary>
    [HttpGet("admins")]
    public async Task<ActionResult<IEnumerable<AdminUserItemDto>>> GetAdmins(CancellationToken cancellationToken)
    {
        var currentAdminId = GetCurrentAdminUserId();

        var admins = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Role == "Admin")
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Select(u => new AdminUserItemDto(
                u.Id,
                u.FullName,
                u.Email,
                u.Role,
                u.CreatedAt,
                u.Id == currentAdminId))
            .ToListAsync(cancellationToken);

        return Ok(admins);
    }

    /// <summary>
    /// Yalnızca normal (Admin OLMAYAN) kayıtlı kullanıcıları ad/e-posta aramasına göre listeler.
    /// </summary>
    [HttpGet("regular")]
    public async Task<ActionResult<IEnumerable<AdminUserItemDto>>> GetRegularUsers(
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users
            .AsNoTracking()
            .Where(u => u.Role != "Admin");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term));
        }

        var users = await query
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.Email)
            .Take(100)
            .Select(u => new AdminUserItemDto(
                u.Id,
                u.FullName,
                u.Email,
                u.Role,
                u.CreatedAt,
                false))
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    /// <summary>
    /// Sıfırdan yeni bir Admin hesabı oluşturur (mevcut admin şifresi doğrulaması zorunludur).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AdminUserItemDto>> CreateAdminUser(
        [FromBody] CreateAdminUserRequest request,
        CancellationToken cancellationToken)
    {
        // Çift şifre birebir doğrulaması (boşluk, büyük/küçük harf dahil her karakter birebir eşleşmelidir)
        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            throw new ArgumentException("Girilen şifreler birbiriyle uyuşmuyor.");
        }

        var (isEmailValid, emailError) = await emailValidationService.ValidateEmailWithMxAsync(request.Email, cancellationToken);
        if (!isEmailValid)
        {
            throw new ArgumentException(emailError ?? "Lütfen geçerli ve aktif bir e-posta adresi girin.");
        }

        var actingAdmin = await VerifyCurrentAdminPasswordAsync(request.CurrentAdminPassword, cancellationToken);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var cleanFullName = request.FullName.Trim();

        var emailExists = await dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (emailExists)
        {
            throw new InvalidOperationException($"'{normalizedEmail}' e-posta adresi ile kayıtlı bir kullanıcı zaten mevcut.");
        }

        var newAdmin = new User
        {
            FullName = cleanFullName,
            Email = normalizedEmail,
            Role = "Admin",
            CreatedAt = DateTime.UtcNow
        };
        newAdmin.PasswordHash = passwordHasher.HashPassword(newAdmin, request.Password);

        dbContext.Users.Add(newAdmin);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Yeni Admin hesabı oluşturuldu: {NewEmail} (İşlemi yapan: {ActingEmail})",
            newAdmin.Email, actingAdmin.Email);

        return CreatedAtAction(nameof(GetAdmins), new { id = newAdmin.Id }, new AdminUserItemDto(
            newAdmin.Id,
            newAdmin.FullName,
            newAdmin.Email,
            newAdmin.Role,
            newAdmin.CreatedAt,
            false));
    }

    /// <summary>
    /// Kayıtlı normal bir kullanıcıya Admin yetkisi verir (mevcut admin şifresi doğrulaması zorunludur).
    /// </summary>
    [HttpPost("{id:int}/grant")]
    public async Task<ActionResult<AdminUserItemDto>> GrantAdminRole(
        int id,
        [FromBody] AdminPasswordConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var actingAdmin = await VerifyCurrentAdminPasswordAsync(request.CurrentAdminPassword, cancellationToken);

        var target = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (target is null)
        {
            throw new KeyNotFoundException($"ID={id} olan kullanıcı bulunamadı.");
        }

        if (string.Equals(target.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Bu kullanıcı zaten Admin yetkisine sahip.");
        }

        target.Role = "Admin";
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin yetkisi verildi: {TargetEmail} (İşlemi yapan: {ActingEmail})",
            target.Email, actingAdmin.Email);

        return Ok(new AdminUserItemDto(
            target.Id,
            target.FullName,
            target.Email,
            target.Role,
            target.CreatedAt,
            target.Id == actingAdmin.Id));
    }

    /// <summary>
    /// Başka bir admin hesabının yetkisini kaldırıp normal kullanıcı yapar.
    /// Aktif oturumdaki adminin kendi yetkisini kaldırması kesinlikle engellenir (403 Forbidden).
    /// </summary>
    [HttpPost("{id:int}/revoke")]
    public async Task<ActionResult<AdminUserItemDto>> RevokeAdminRole(
        int id,
        [FromBody] AdminPasswordConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var currentAdminId = GetCurrentAdminUserId();
        if (id == currentAdminId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Aktif oturumdaki kendi admin yetkinizi kaldıramazsınız."
            });
        }

        var actingAdmin = await VerifyCurrentAdminPasswordAsync(request.CurrentAdminPassword, cancellationToken);

        var target = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (target is null)
        {
            throw new KeyNotFoundException($"ID={id} olan kullanıcı bulunamadı.");
        }

        if (!string.Equals(target.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Bu kullanıcı zaten normal kullanıcı rolünde.");
        }

        target.Role = "User";
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin yetkisi kaldırıldı: {TargetEmail} -> User (İşlemi yapan: {ActingEmail})",
            target.Email, actingAdmin.Email);

        return Ok(new AdminUserItemDto(
            target.Id,
            target.FullName,
            target.Email,
            target.Role,
            target.CreatedAt,
            false));
    }
}
