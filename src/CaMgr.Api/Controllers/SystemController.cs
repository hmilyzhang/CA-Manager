using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

/// <summary>mode: "http" (default) | "https" | "both"</summary>
public record SystemModeRequest(string Mode, int? HttpPort, int? HttpsPort, string? Thumbprint);

[ApiController]
[Route("api/system")]
[Authorize]
[RequireRole(AppRole.Admin)]
public sealed class SystemController(ILogger<SystemController> log) : Controller
{
    private const string AppId = "{4d8a5f2e-6b3c-4a9e-9f2e-ca7mgr000001}";
    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.Production.json");

    public sealed record EndpointInfo(string Url, int Port);

    /// <summary>Current listener configuration parsed from appsettings.Production.json.</summary>
    [HttpGet("endpoints")]
    public IActionResult Endpoints()
    {
        var (http, https) = ReadEndpoints();
        return Ok(new
        {
            mode = https is null ? "http" : http is null ? "https" : "both",
            httpPort = http?.Port,
            httpsPort = https?.Port,
            currentUrls = new[] { http?.Url, https?.Url }.Where(u => u is not null).ToList(),
        });
    }

    /// <summary>Machine certificates usable for HTTPS (Server Authentication EKU, private key, not expired).</summary>
    [HttpGet("certs")]
    public IActionResult Certs()
    {
        var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var now = DateTime.Now;
        const string ServerAuth = "1.3.6.1.5.5.7.3.1";
        var selectable = new List<object>();
        var excluded = new List<object>();
        foreach (var c in store.Certificates.Cast<X509Certificate2>())
        {
            string? reason = null;
            if (c.NotAfter <= now) reason = "expired";
            else if (!c.HasPrivateKey) reason = "no-private-key";
            else if (c.Extensions.OfType<X509BasicConstraintsExtension>().Any(bc => bc.CertificateAuthority))
                reason = "ca-cert";
            else if (!c.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                .Any(e => e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>()
                    .Any(o => o.Value == ServerAuth)))
                reason = "no-server-auth";

            var item = new
            {
                thumbprint = c.Thumbprint,
                subject = c.Subject,
                issuer = c.Issuer,
                notAfter = c.NotAfter,
                san = c.Extensions.OfType<X509SubjectAlternativeNameExtension>()
                    .SelectMany(e => e.EnumerateDnsNames()).ToList(),
                reason,
            };
            if (reason is null) selectable.Add(item); else excluded.Add(item);
        }
        store.Close();
        return Ok(new { certs = selectable, excluded });
    }

    /// <summary>
    /// Applies the listener mode (http / https / both), binds the certificate for HTTPS
    /// via netsh, rewrites appsettings.Production.json and restarts the service
    /// (SCM failure actions bring it back within seconds).
    /// </summary>
    [HttpPost("apply")]
    public IActionResult Apply([FromBody] SystemModeRequest req)
    {
        var httpPort = req.HttpPort ?? 8443;
        var httpsPort = req.HttpsPort ?? 8443;

        switch (req.Mode)
        {
            case "http":
                if (httpPort is < 1 or > 65535) return BadRequest(new { error = "HTTP 端口无效" });
                break;
            case "https":
            case "both":
                if (httpsPort is < 1 or > 65535) return BadRequest(new { error = "HTTPS 端口无效" });
                if (req.Mode == "both" && httpPort is < 1 or > 65535) return BadRequest(new { error = "HTTP 端口无效" });
                if (req.Mode == "both" && httpsPort == httpPort) return BadRequest(new { error = "HTTP 与 HTTPS 端口不能相同" });
                // certificate must exist in the machine store
                var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var cert = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(c => c.Thumbprint == req.Thumbprint);
                store.Close();
                if (cert is null) return BadRequest(new { error = "请选择本机证书存储中的证书" });

                RunNetsh($"http delete sslcert ipport=0.0.0.0:{httpsPort}");
                var (ok, output) = RunNetsh($"http add sslcert ipport=0.0.0.0:{httpsPort} certhash={req.Thumbprint} appid={AppId} certstorename=MY");
                if (!ok && !output.Contains("already", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = $"netsh 绑定失败: {output.Trim()}" });
                break;
            default:
                return BadRequest(new { error = "模式无效" });
        }

        WriteEndpoints(req.Mode, httpPort, httpsPort);

        Task.Run(async () =>
        {
            await Task.Delay(800);
            log.LogWarning("Listener mode {Mode} applied - restarting service to re-listen", req.Mode);
            Environment.Exit(1); // triggers the configured failure action (auto-restart)
        });

        var reopened = req.Mode switch
        {
            "http" => $"http://+:{httpPort}",
            "https" => $"https://+:{httpsPort}",
            _ => $"http://+:{httpPort} + https://+:{httpsPort}",
        };
        return Ok(new { message = $"监听模式已切换为 {req.Mode}，服务正在自动重启（5-10 秒）。重新打开: {reopened}" });
    }

    private static (EndpointInfo? http, EndpointInfo? https) ReadEndpoints()
    {
        EndpointInfo? Parse(JsonElement e)
        {
            if (!e.TryGetProperty("Url", out var u)) return null;
            var url = u.GetString();
            if (url is null) return null;
            var m = System.Text.RegularExpressions.Regex.Match(url, @":(\d+)$");
            return new EndpointInfo(url, m.Success ? int.Parse(m.Groups[1].Value) : 0);
        }

        if (!System.IO.File.Exists(SettingsPath)) return (new EndpointInfo("http://+:8443", 8443), null);
        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(SettingsPath));
            if (!doc.RootElement.TryGetProperty("Kestrel", out var k) ||
                !k.TryGetProperty("Endpoints", out var eps)) return (new EndpointInfo("http://+:8443", 8443), null);

            EndpointInfo? http = eps.TryGetProperty("Http", out var h) ? Parse(h) : null;
            EndpointInfo? https = eps.TryGetProperty("Https", out var hs) ? Parse(hs) : null;
            return (http, https);
        }
        catch { return (new EndpointInfo("http://+:8443", 8443), null); }
    }

    private static void WriteEndpoints(string mode, int httpPort, int httpsPort)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartObject("Kestrel");
            w.WriteStartObject("Endpoints");
            if (mode is "http" or "both")
            {
                w.WriteStartObject("Http");
                w.WriteString("Url", $"http://+:{httpPort}");
                w.WriteEndObject();
            }
            if (mode is "https" or "both")
            {
                w.WriteStartObject("Https");
                w.WriteString("Url", $"https://+:{httpsPort}");
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        System.IO.File.WriteAllText(SettingsPath, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
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
