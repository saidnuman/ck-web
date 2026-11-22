using System.ComponentModel.DataAnnotations;

namespace ProductShowcase.Api.DTOs;

public record AdminUserItemDto(
    int Id,
    string FullName,
    string Email,
    string Role,
    DateTime CreatedAt,
    bool IsCurrentUser
);

public record AdminPasswordConfirmRequest(
    [Required(ErrorMessage = "İşlemi onaylamak için mevcut admin şifrenizi girin.")]
    string CurrentAdminPassword
);

public record CreateAdminUserRequest(
    [Required(ErrorMessage = "Ad / Kullanıcı Adı zorunludur.")]
    [MinLength(2, ErrorMessage = "Ad / Kullanıcı Adı en az 2 karakter olmalıdır.")]
    [MaxLength(120)]
    string FullName,

    [Required(ErrorMessage = "E-posta adresi zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [MaxLength(200)]
    string Email,

    [Required(ErrorMessage = "Yeni admin parolası zorunludur.")]
    [MinLength(6, ErrorMessage = "Parola en az 6 karakter olmalıdır.")]
    [MaxLength(100)]
    string Password,

    [Required(ErrorMessage = "Şifre tekrarı zorunludur.")]
    [MaxLength(100)]
    string ConfirmPassword,

    [Required(ErrorMessage = "İşlemi onaylamak için mevcut admin şifrenizi girin.")]
    string CurrentAdminPassword
);
