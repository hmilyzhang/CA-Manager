using CaMgr.Api.CaInterop;
using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Windows service hosting (no-op when run interactively)
builder.Host.UseWindowsService(options => options.ServiceName = "CA-Manager");

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);

builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(dataDir, "camgr.db")}"));

builder.Services.AddSingleton<StaComScheduler>();
builder.Services.AddSingleton<CaContext>();
builder.Services.AddSingleton<CaDbService>();
builder.Services.AddSingleton<CaAdminService>();
builder.Services.AddSingleton<CaRequestService>();
builder.Services.AddSingleton<CertificateService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<LdapAuthService>();
builder.Services.AddSingleton<MailService>();
builder.Services.AddSingleton<NotifyService>();
builder.Services.AddSingleton<SelfServiceCertService>();
builder.Services.AddSingleton<ApprovalService>();
builder.Services.AddSingleton<PgpService>();
builder.Services.AddHostedService<NotifyBackgroundService>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "camgr_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.LoginPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// create schema + seed initial admin
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    // schema patch for existing databases (EnsureCreated does not alter existing tables)
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "ApprovalRequests" (
            "Id" INTEGER NOT NULL CONSTRAINT "pk_ApprovalRequests" PRIMARY KEY AUTOINCREMENT,
            "Username" TEXT NOT NULL,
            "Type" TEXT NOT NULL,
            "Template" TEXT NOT NULL,
            "CommonName" TEXT NOT NULL,
            "SanCsv" TEXT NOT NULL,
            "KeyAlgorithm" TEXT NOT NULL,
            "CsrBase64" TEXT NOT NULL,
            "KeyBlob" BLOB NULL,
            "Status" TEXT NOT NULL,
            "RequestId" INTEGER NULL,
            "Disposition" INTEGER NULL,
            "Comment" TEXT NOT NULL DEFAULT '',
            "DecidedBy" TEXT NOT NULL DEFAULT '',
            "DecidedAt" TEXT NULL,
            "CreatedAt" TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_ApprovalRequests_Status" ON "ApprovalRequests" ("Status");
        """);
    var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
    await auth.SeedAdminAsync();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// SPA fallback: any non-API route serves index.html
app.MapFallbackToFile("index.html");

app.Run();
