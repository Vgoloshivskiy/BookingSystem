using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BookingSystem.Tests;

/// <summary>
/// Boots the real ASP.NET Core pipeline (real controllers, real BookingService, real
/// ApplicationDbContext) against a fresh, file-backed SQLite database per test run.
/// A real file (not ":memory:") is used deliberately: file-mode SQLite gives each
/// HttpClient/connection genuinely independent connections that must coordinate through
/// the database's own locking, which is what the concurrency test needs to exercise.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"booking-test-{Guid.NewGuid()}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Sqlite"] = $"Data Source={_dbPath};Cache=Shared",
                ["Jwt:Key"] = "test-signing-key-at-least-32-characters-long!!",
                ["Jwt:Issuer"] = "BookingSystem",
                ["Jwt:Audience"] = "BookingSystemClient"
            });
        });
        // No ConfigureServices override needed: Program.cs already reads
        // "Database:Provider" and "ConnectionStrings:Sqlite" from configuration, and the
        // in-memory collection above (added last) takes priority over appsettings.json.
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch { /* best effort cleanup */ }
    }
}
