using BookingSystem.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Api.Data;

/// <summary>
/// Idempotent startup seeding: roles, a default admin account, and a couple of sample
/// resources with a week of half-hour slots so the app is usable immediately after deploy.
/// </summary>
public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        // NOTE: this repo does not ship EF Core migration files (the sandbox this repo
        // was authored in had no .NET SDK to run `dotnet ef migrations add`), so
        // EnsureCreatedAsync is used to build the schema directly from the current model
        // instead of Database.MigrateAsync(). This works for a fresh database (a new
        // Azure SQL Database, or a fresh SQLite file per test run) but does NOT support
        // incremental schema evolution. Before evolving the model further, run:
        //   dotnet ef migrations add InitialCreate --project src/BookingSystem.Api
        // once, then switch this back to db.Database.MigrateAsync() and use
        // `dotnet ef database update` for subsequent schema changes (see CLAUDE.md).
        await db.Database.EnsureCreatedAsync();

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.Admin, Roles.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        const string adminEmail = "admin@bookingsystem.local";
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = "System Administrator",
                EmailConfirmed = true
            };
            // NOTE: this is a seed/demo password for reviewer convenience only.
            // Change it (or remove this seed) before any real deployment.
            var result = await userManager.CreateAsync(admin, "Admin123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, Roles.Admin);
            }
        }

        if (!await db.Resources.AnyAsync())
        {
            var rooms = new[]
            {
                new Resource { Name = "Falcon", Description = "4-person room, 2nd floor", Location = "Floor 2", Capacity = 4 },
                new Resource { Name = "Orion", Description = "10-person room with projector", Location = "Floor 3", Capacity = 10 }
            };
            db.Resources.AddRange(rooms);
            await db.SaveChangesAsync();

            var today = DateTime.UtcNow.Date.AddDays(1);
            foreach (var room in rooms)
            {
                for (var day = 0; day < 3; day++)
                {
                    for (var hour = 9; hour < 17; hour++)
                    {
                        var start = today.AddDays(day).AddHours(hour);
                        db.TimeSlots.Add(new TimeSlot
                        {
                            ResourceId = room.Id,
                            StartUtc = start,
                            EndUtc = start.AddHours(1),
                            IsBooked = false
                        });
                    }
                }
            }
            await db.SaveChangesAsync();
        }
    }
}
