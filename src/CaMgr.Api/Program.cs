using CaMgr.Api.CaInterop;
using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

// Emergency admin password reset (run from the publish folder while the service may stay running):
//   CaMgr.Api.exe reset-admin              -> generates a temporary password, prints it, forces change at next login
//   CaMgr.Api.exe reset-admin <password>   -> sets the given password (min 8 chars)
if (args.Length > 0 && args[0] == "reset-admin")
{
    var resetDataDir = Path.Combine(AppContext.BaseDirectory, "data");
    Directory.CreateDirectory(resetDataDir);
    var dbPath = Path.Combine(resetDataDir, "camgr.db");
    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Database not found: {dbPath}");
        return 1;
    }
    var newPassword = args.Length > 1 ? args[1] : "Adm-" + Guid.NewGuid().ToString("N")[..10];
    if (newPassword.Length < 8)
    {
        Console.Error.WriteLine("Password must be at least 8 characters.");
        return 1;
    }
    var hasher = new PasswordHasher<UserEntity>();
    var hash = hasher.HashPassword(new UserEntity { Username = "admin" }, newPassword);
    using (var conn = new SqliteConnection($"Data Source={dbPath}"))
    {
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Users
            SET PasswordHash = $h, MustChangePassword = 1, FailedAttempts = 0, LockedUntil = NULL
            WHERE Username = 'admin' AND Source = 0
            """;
        cmd.Parameters.AddWithValue("$h", hash);
        if (cmd.ExecuteNonQuery() == 0)
        {
            Console.Error.WriteLine("Local 'admin' account not found.");
            return 1;
        }
    }
    Console.WriteLine();
    Console.WriteLine("Admin password has been reset.");
    Console.WriteLine($"  Username: admin");
    Console.WriteLine($"  New password: {newPassword}");
    Console.WriteLine("  (change forced at next login; no service restart needed)");
    return 0;
}

var builder = WebApplication.CreateBuilder(args);

// Windows service hosting (no-op when run interactively)
builder.Host.UseWindowsService(options => options.ServiceName = "CA-Manager");

// Listener configuration: section "Listeners" in appsettings(.Production).json
//   { "Listeners": { "Mode": "http|https|both", "HttpPort": 8442, "HttpsPort": 8444, "Thumbprint": "<sha1>" } }
// Absent section = HTTP on 8442 (legacy default).
var listenerMode = builder.Configuration["Listeners:Mode"] ?? "http";
var listenerHttpPort = int.TryParse(builder.Configuration["Listeners:HttpPort"], out var lhp) ? lhp : 8442;
var listenerHttpsPort = int.TryParse(builder.Configuration["Listeners:HttpsPort"], out var lsp) ? lsp : 8444;
var listenerThumbprint = builder.Configuration["Listeners:Thumbprint"];

builder.WebHost.ConfigureKestrel(o =>
{
    if (listenerMode is "http" or "both")
        o.ListenAnyIP(listenerHttpPort);

    if (listenerMode is "https" or "both")
    {
        X509Certificate2? cert = null;
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            cert = store.Certificates.Cast<X509Certificate2>()
                .FirstOrDefault(c => c.Thumbprint == listenerThumbprint && c.HasPrivateKey);
            store.Close();
        }
        catch { /* fall through to the fallback below */ }

        if (cert is not null)
        {
            o.ListenAnyIP(listenerHttpsPort, l => l.UseHttps(cert));
        }
        else
        {
            // never crash-loop over a bad certificate: fall back to plain HTTP and log loudly
            o.ListenAnyIP(listenerHttpPort);
            var logger = o.ApplicationServices.GetRequiredService<ILogger<Program>>();
            logger.LogError("HTTPS certificate '{Thumbprint}' not found in LocalMachine\\My - falling back to HTTP on port {Port}. " +
                            "Pick a certificate in System settings and re-apply.", listenerThumbprint, listenerHttpPort);
        }
    }
});

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

return 0;
