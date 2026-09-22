using System.Text;
using BookingSystem.Api.Data;
using BookingSystem.Api.Hubs;
using BookingSystem.Api.Models;
using BookingSystem.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---------------------------------------------------------------
// Provider is swappable via configuration: "Sqlite" for local dev / the automated
// concurrency test, "SqlServer" for Azure SQL in production. The concurrency-control
// logic in BookingService is plain ANSI SQL (a single UPDATE ... WHERE statement) and
// behaves identically on both providers.
var dbProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (dbProvider == "SqlServer")
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"));
    }
    else
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=bookingsystem.db");
    }
});

// --- Identity ----------------------------------------------------------------
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// --- JWT auth ------------------------------------------------------------------
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!))
    };

    // Allow SignalR clients to authenticate via an access_token query string param,
    // since browser WebSocket/EventSource connections can't set an Authorization header.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/booking"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// --- SignalR -------------------------------------------------------------------
// AddAzureSignalR() is a no-op fallback to local in-process SignalR when no connection
// string is configured, so the app also runs fine locally / in the concurrency test
// without provisioning Azure SignalR Service.
var signalRBuilder = builder.Services.AddSignalR();
var azureSignalRConnection = builder.Configuration.GetConnectionString("AzureSignalR");
if (!string.IsNullOrWhiteSpace(azureSignalRConnection))
{
    signalRBuilder.AddAzureSignalR(azureSignalRConnection);
}

// --- App services ----------------------------------------------------------------
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<BookingHub>("/hubs/booking");

using (var scope = app.Services.CreateScope())
{
    await SeedData.InitializeAsync(scope.ServiceProvider);
}

app.Run();

// Exposed for WebApplicationFactory<Program> in the test project.
public partial class Program { }
