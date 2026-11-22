using System.ComponentModel.DataAnnotations;

namespace ProductShowcase.Api.Entities;

/// <summary>
/// "Şifremi Unuttum" akışı için tek kullanımlık, 15 dakika geçerli 6 haneli doğrulama kodlarının SHA-256 özetini saklar.
/// </summary>
public class PasswordResetCode
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool IsUsed { get; set; } = false;

    public int AttemptCount { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
