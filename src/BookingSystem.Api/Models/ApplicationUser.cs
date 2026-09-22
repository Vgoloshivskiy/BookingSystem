using Microsoft.AspNetCore.Identity;

namespace BookingSystem.Api.Models;

/// <summary>
/// Application user, extending ASP.NET Core Identity's default user with a display name.
/// Roles ("Admin" / "User") are managed separately via IdentityRole and UserManager.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Well-known role names used throughout the application. Centralised here so
/// controllers and seed data never rely on magic strings scattered around the codebase.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}
