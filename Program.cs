using System.Text;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Npgsql;
using Api.Data;
using Api.Models;
using Api.Services;
using Api.Middleware;


DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Database
var config = builder.Configuration;
var connectionString = new NpgsqlConnectionStringBuilder
{
    Host = config["POSTGRES_HOST"] ?? "localhost",
    Port = int.Parse(config["DB_PORT"] ?? "5432"),
    Database = config["POSTGRES_DB"],
    Username = config["POSTGRES_USER"],
    Password = config["POSTGRES_PASSWORD"],
}.ConnectionString;

builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// Add services to the container.
builder.Services.AddControllers().AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention());

builder.Services
    .AddIdentityCore<User>(options =>
    {
        // Email is the login identifier, so it must be unique
        options.User.RequireUniqueEmail = true;

        // No login until the email is confirmed (enforces the Phase 3 flow)
        options.SignIn.RequireConfirmedEmail = true;

        // Password policy — length-first, per modern (NIST) guidance
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;

        // Lockout — brute-force protection
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddSignInManager();

builder.Services
  .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer(options =>
  {
      options.MapInboundClaims = false;
      options.TokenValidationParameters = new TokenValidationParameters
      {
          ValidateIssuer = true,
          ValidIssuer = config["JWT_ISSUER"],
          ValidateAudience = true,
          ValidAudience = config["JWT_AUDIENCE"],
          ValidateIssuerSigningKey = true,
          IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SIGNING_KEY"]!)),
          ValidateLifetime = true,
          ClockSkew = TimeSpan.FromSeconds(30)
      };
  }).AddJwtBearer("Enrollment", options =>
  {
      options.MapInboundClaims = false;
      options.TokenValidationParameters = new TokenValidationParameters
      {
          ValidateIssuer = true,
          ValidIssuer = config["JWT_ISSUER"],
          ValidateAudience = true,
          ValidAudience = TokenService.EnrollmentAudience,
          ValidateIssuerSigningKey = true,
          IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SIGNING_KEY"]!)),
          ValidateLifetime = true,
          ClockSkew = TimeSpan.FromSeconds(30)
      };
  });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();

        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many requests. Please slow down and try again later." }, ct);
    };

    // Login: moderate — slows credential spraying without punishing shared office IPs too hard.
    options.AddPolicy("login", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    // 2FA: strict — the challenge token only lives 5 minutes, so 5 guesses per IP per window
    // makes brute-forcing a 6-digit code hopeless.
    options.AddPolicy("mfa", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));

    // Forgot/reset: tight — prevents using your mailer to bomb a victim's inbox.
    options.AddPolicy("password-reset", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
});

// Keys default to the local filesystem, which doesn't survive a container rebuild/redeploy —
// every reset invalidates any confirm-email/reset-password token issued before it. Persist
// to Postgres instead (already have it) via AppDbContext's IDataProtectionKeyContext.
// SetApplicationName pins the key ring to a stable name regardless of content-root path,
// which otherwise differs between `dotnet run` (repo path) and the container (/app) — without
// it, each is treated as a separate "application" for discriminator purposes.
builder.Services.AddDataProtection()
    .SetApplicationName("LedgrApi")
    .PersistKeysToDbContext<AppDbContext>();

// Falls back to logging (instead of failing to start) when RESEND_API_KEY isn't set yet —
// e.g. a fresh clone before anyone's signed up for Resend. Set both RESEND_API_KEY and
// RESEND_FROM to switch a running environment over to actually sending mail.
if (!string.IsNullOrWhiteSpace(config["RESEND_API_KEY"]))
{
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
    {
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", config["RESEND_API_KEY"]);
    });
}
else
{
    builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
}
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Applies any pending EF Core migrations on startup — the Docker/compose Postgres
    // container starts with an empty database, so the schema has to come from somewhere.
    // Fine for a single API instance; if this ever runs as multiple replicas, migrating
    // from every instance's startup would race — move this to a one-off init step first.
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

    foreach (var roleName in new[] { "Admin", "User" })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
    }

    // Optional: promote a known account to Admin so you can test.
    // Set SEED_ADMIN_EMAIL in .env to an email you've already registered.
    var adminEmail = Environment.GetEnvironmentVariable("SEED_ADMIN_EMAIL");
    if (!string.IsNullOrWhiteSpace(adminEmail))
    {
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is not null && !await userManager.IsInRoleAsync(admin, "Admin"))
            await userManager.AddToRoleAsync(admin, "Admin");
    }
}

// Configure the HTTP request pipeline.
app.UseSecurityHeaders();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.Run();

public partial class Program { }