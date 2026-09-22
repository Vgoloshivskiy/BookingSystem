using BookingSystem.Api.Models;

namespace BookingSystem.Api.Services;

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateToken(ApplicationUser user, IList<string> roles);
}
