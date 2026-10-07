using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

public record HttpsBindRequest(string Thumbprint, int? Port);

[ApiController]
[Route("api/system")]
[Authorize]
[RequireRole(AppRole.Admin)]
public sealed class SystemController(ILogger<SystemController> log) : Controller
{
    private const string AppId = "{4d8a5f2e-6b3c-4a9e-9f2e-ca7mgr000001}";

    /// <summary>Machine certificates usable for HTTPS (Server Authentication EKU, private key, not expired).</summary>
    [HttpGet("certs")]
    public IActionResult Certs()
    {
        var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var now = DateTime.Now;
        var certs = store.Certificates.Cast<X509Certificate2>()
            .Where(c => c.HasPrivateKey && c.NotAfter > now)
            .Where(c => c.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                .Any(e => e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>()
                    .Any(o => o.Value == "1.3.6.1.5.5.7.3.1")))
            .OrderByDescending(c => c.NotAfter)
            .Select(c => new
            {
                thumbprint = c.Thumbprint,
                subject = c.Subject,
                issuer = c.Issuer,
                notAfter = c.NotAfter,
                san = c.Extensions.OfType<X509SubjectAlternativeNameExtension>()
                    .SelectMany(e => e.EnumerateDnsNames()).ToList(),
            })
            .ToList();
        store.Close();

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Production.json");
        string? current = null;
        if (System.IO.File.Exists(settingsPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(settingsPath));
                current = doc.RootElement.TryGetProperty("Kestrel", out var k)
                       && k.TryGetProperty("Endpoints", out var e)
                       && e.TryGetProperty("Http", out var h)
                       && h.TryGetProperty("Url", out var u) ? u.GetString() : null;
            }
            catch { /* corrupt settings file */ }
        }
        return Ok(new { certs, current });
    }

    /// <summary>Binds the chosen certificate to the HTTPS port, updates the production config,
    /// then restarts the service (failure actions bring it back within seconds).</summary>
    [HttpPost("https")]
    public IActionResult BindHttps([FromBody] HttpsBindRequest req)
    {
        var port = req.Port ?? 8443;
        if (port is < 1 or > 65535) return BadRequest(new { error = "端口无效" });

        var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var cert = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(c => c.Thumbprint == req.Thumbprint);
        store.Close();
        if (cert is null) return BadRequest(new { error = "本机证书存储中找不到该指纹的证书" });

        // 1. sslcert binding (delete stale binding for the port first)
        RunNetsh($"http delete sslcert ipport=0.0.0.0:{port}");
        var (ok, output) = RunNetsh($"http add sslcert ipport=0.0.0.0:{port} certhash={req.Thumbprint} appid={AppId} certstorename=MY");
        if (!ok && !output.Contains("already", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"netsh 绑定失败: {output.Trim()}" });

        // 2. switch the production config to https (the file only carries the Kestrel endpoint)
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Production.json");
        using (var ms = new MemoryStream())
        {
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteStartObject("Kestrel");
                w.WriteStartObject("Endpoints");
                w.WriteStartObject("Http");
                w.WriteString("Url", $"https://+:{port}");
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();
            }
            System.IO.File.WriteAllText(settingsPath, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
        }

        // 3. schedule self-restart (SCM failure actions bring the service back)
        Task.Run(async () =>
        {
            await Task.Delay(800);
            log.LogWarning("HTTPS binding applied - restarting service to re-listen on https://+:{Port}", port);
            Environment.Exit(1); // non-zero exit triggers the configured failure action (restart)
        });

        return Ok(new
        {
            message = $"HTTPS 已绑定到端口 {port}，服务正在自动重启（约 5-10 秒），请稍后刷新页面。",
            url = $"https://+: {port}".Replace("+ :", "+"),
        });
    }

    private static (bool ok, string output) RunNetsh(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("netsh", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return (p.ExitCode == 0, output);
    }
}
