using ProductShowcase.Api.Entities;

namespace ProductShowcase.Api.Services;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}
