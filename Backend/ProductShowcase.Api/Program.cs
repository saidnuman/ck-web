using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.IdentityModel.Tokens;
using ProductShowcase.Api.Data;
using ProductShowcase.Api.Entities;
using ProductShowcase.Api.Middleware;
using ProductShowcase.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Yerel gizli yapılandırma (GitHub'a gitmeyen appsettings.Local.json) ve ortam değişkenleri (ENV) desteği
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// 1. Controller, Multipart/Form limitleri ve OpenAPI (.NET 10) servisleri
builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 105 * 1024 * 1024; // 105 MB
    options.ValueLengthLimit = int.MaxValue; // Büyük SlotsJson yükleri için
    options.MultipartHeadersLengthLimit = int.MaxValue;
});
builder.Services.AddOpenApi();

// 2. MySQL + Entity Framework Core (Code-First) yapılandırması
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection bulunamadı.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySQL(connectionString)
           .ReplaceService<IHistoryRepository, MariaDbCompatibleHistoryRepository>());

// 3. Uygulama servisleri (Fotoğraf/Video yükleme, Kategori Şablon Klonlama, JWT üretimi, Şifre hashleme, E-posta MX & SMTP)
static bool IsConfiguredValue(string? val) =>
    !string.IsNullOrWhiteSpace(val) &&
    !val.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase) &&
    !val.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

var useCloudinaryStorage =
    IsConfiguredValue(builder.Configuration["Cloudinary:CloudName"]) &&
    IsConfiguredValue(builder.Configuration["Cloudinary:ApiKey"]) &&
    IsConfiguredValue(builder.Configuration["Cloudinary:ApiSecret"]);

if (useCloudinaryStorage)
{
    builder.Services.AddScoped<IFileStorageService, CloudinaryFileStorageService>();
}
else
{
    builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
}
builder.Services.AddScoped<ICategoryTemplateService, CategoryTemplateService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<PasswordHasher<User>>();
builder.Services.AddSingleton<IEmailValidationService, EmailValidationService>();
builder.Services.AddScoped<IEmailSenderService, SmtpEmailSenderService>();

// 4. JWT (JSON Web Token) Kimlik Doğrulama yapılandırması
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"]
    ?? throw new InvalidOperationException("Jwt:Key yapılandırması eksik.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
    options.Events = new JwtBearerEvents
    {
        // Admin yetki kaldırma / rol değişikliğinin MEVCUT oturumlarda da anında etkili olması için
        // her doğrulanmış JWT isteğinde güncel rolü MySQL'den senkronize et
        OnTokenValidated = async context =>
        {
            var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
            {
                context.Fail("Geçersiz kullanıcı kimliği.");
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var dbUser = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.Role })
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            if (dbUser is null)
            {
                context.Fail("Kullanıcı hesabı artık mevcut değil.");
                return;
            }

            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                var existingRoleClaims = identity.FindAll(ClaimTypes.Role)
                    .Concat(identity.FindAll("role"))
                    .ToList();
                foreach (var rc in existingRoleClaims)
                {
                    identity.RemoveClaim(rc);
                }
                identity.AddClaim(new Claim(ClaimTypes.Role, dbUser.Role));
            }
        }
    };
});

builder.Services.AddAuthorization();

// 5. CORS politikası (Statik HTML/JS frontend + GitHub Pages canlı erişimi için)
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var rawOriginsString = builder.Configuration["Cors:AllowedOrigins"];
var allowedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "https://saidnuman.github.io"
};
foreach (var originItem in configuredOrigins)
{
    var trimmed = originItem?.Trim().TrimEnd('/');
    if (!string.IsNullOrWhiteSpace(trimmed)) allowedSet.Add(trimmed);
}
if (!string.IsNullOrWhiteSpace(rawOriginsString))
{
    foreach (var originItem in rawOriginsString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var trimmed = originItem.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(trimmed)) allowedSet.Add(trimmed);
    }
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCorsPolicy", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Geliştirme ortamında Live Server, localhost ve file:// (null origin) erişimine izin verir
            policy.SetIsOriginAllowed(_ => true)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            policy.SetIsOriginAllowed(origin =>
                    string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase) ||
                    allowedSet.Contains(origin.TrimEnd('/')) ||
                    (Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                     (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1")))
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

var app = builder.Build();

// Yüklenen ürün fotoğrafları için wwwroot/uploads klasörünü garanti altına al
var webRootPath = string.IsNullOrWhiteSpace(app.Environment.WebRootPath)
    ? Path.Combine(app.Environment.ContentRootPath, "wwwroot")
    : app.Environment.WebRootPath;
Directory.CreateDirectory(Path.Combine(webRootPath, "uploads"));

// 6. Global Hata Yakalama ve İstek Loglama Middleware'leri
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 7. Yüklenen fotoğrafların (/uploads/...) tarayıcıda görüntülenebilmesi için StaticFiles
app.UseStaticFiles();

// 8. CORS -> Authentication -> Authorization sırası
app.UseCors("FrontendCorsPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 9. Sağlık kontrolü endpoint'leri (Render Health Check için /health + /api/health)
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    timestamp = DateTime.UtcNow
}));

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    framework = ".NET 10",
    storage = useCloudinaryStorage ? "cloudinary" : "local",
    timestamp = DateTime.UtcNow
}));

// 10. Otomatik Migration ve varsayılan veri (Seed) yükleme (Yerel & Aiven MySQL)
if (builder.Configuration.GetValue<bool?>("Database:AutoMigrateAndSeed") ?? true)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    logger.LogInformation("Aktif medya depolama sağlayıcısı: {StorageProvider}",
        useCloudinaryStorage ? "Cloudinary (Kalıcı Bulut Depolama)" : "LocalFileStorage (wwwroot/uploads)");

    try
    {
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync();
        }
        if (await db.Database.CanConnectAsync())
        {
            await DbSeeder.SeedAsync(db, logger, builder.Configuration);
            var templateService = scope.ServiceProvider.GetRequiredService<ICategoryTemplateService>();
            await templateService.InitializeAllExistingCategoriesAsync();
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex,
            "MySQL veritabanına başlangıçta bağlanılamadı veya tablolar henüz oluşturulmadı. " +
            "MySQL bağlantınızı kontrol edip 'dotnet ef database update' komutunu çalıştırın.");
    }
}

app.Run();
