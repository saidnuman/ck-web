using System.ComponentModel.DataAnnotations;

namespace ProductShowcase.Api.DTOs;

public record RegisterRequest(
    [Required(ErrorMessage = "Ad soyad zorunludur.")]
    [MaxLength(120)]
    string FullName,

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    string Email,

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [MinLength(6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
    string Password
);

public record LoginRequest(
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    string Email,

    [Required(ErrorMessage = "Şifre zorunludur.")]
    string Password
);

public record AuthUserDto(
    int Id,
    string FullName,
    string Email,
    string Role
);

public record AuthResponse(
    string Token,
    DateTime ExpiresAt,
    AuthUserDto User
);

public record GoogleClientIdResponse(
    string ClientId,
    bool Enabled
);

public record GoogleAuthRequest(
    [Required(ErrorMessage = "Google kimlik belirteci (idToken) zorunludur.")]
    string IdToken
);

public record ForgotPasswordRequest(
    [Required(ErrorMessage = "E-posta adresi zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    string Email
);

public record ResetPasswordRequest(
    [Required(ErrorMessage = "E-posta adresi zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    string Email,

    [Required(ErrorMessage = "6 haneli doğrulama kodu zorunludur.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Doğrulama kodu 6 haneli sayısal olmalıdır.")]
    string Code,

    [Required(ErrorMessage = "Yeni şifre zorunludur.")]
    [MinLength(6, ErrorMessage = "Yeni şifre en az 6 karakter olmalıdır.")]
    [MaxLength(100)]
    string NewPassword,

    [Required(ErrorMessage = "Yeni şifre tekrarı zorunludur.")]
    [MaxLength(100)]
    string ConfirmNewPassword
);
