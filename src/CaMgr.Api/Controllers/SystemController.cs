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
    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.Production.json");
    private const int DefaultHttpPort = 8442;

    public sealed record ListenerInfo(string Mode, int HttpPort, int HttpsPort, string? Thumbprint);

    private static ListenerInfo Current()
    {
        try
        {
            if (System.IO.File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("Listeners", out var l))
                {
                    string Get(string name, string def) =>
                        l.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()! : def;
                    return new ListenerInfo(
                        Get("Mode", "http"),
                        int.TryParse(Get("HttpPort", DefaultHttpPort.ToString()), out var p) ? p : DefaultHttpPort,
                        int.TryParse(Get("HttpsPort", "8444"), out var p2) ? p2 : 8444,
                        l.TryGetProperty("Thumbprint", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null);
                }
            }
        }
        catch { /* corrupt file -> defaults */ }
        return new ListenerInfo("http", DefaultHttpPort, 8444, null);
    }

    /// <summary>Current listener configuration.</summary>
    [HttpGet("endpoints")]
    public IActionResult Endpoints()
    {
        var cur = Current();
        var urls = cur.Mode switch
        {
            "https" => new[] { $"https://+:{cur.HttpsPort}" },
            "both" => new[] { $"http://+:{cur.HttpPort}", $"https://+:{cur.HttpsPort}" },
            _ => new[] { $"http://+:{cur.HttpPort}" },
        };
        return Ok(new
        {
            mode = cur.Mode,
            httpPort = cur.HttpPort,
            httpsPort = cur.HttpsPort,
            thumbprint = cur.Thumbprint,
            currentUrls = urls,
        });
    }

    /// <summary>Machine certificates usable for HTTPS (Server Authentication EKU, private key, not expired)
    /// plus the non-selectable ones with the exclusion reason.</summary>
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
    /// Applies the listener mode by rewriting the Listeners section and restarting the service.
    /// Kestrel loads the certificate by thumbprint from the machine store at startup.
    /// </summary>
    [HttpPost("apply")]
    public IActionResult Apply([FromBody] SystemModeRequest req)
    {
        var httpPort = req.HttpPort ?? DefaultHttpPort;
        var httpsPort = req.HttpsPort ?? 8444;

        switch (req.Mode)
        {
            case "http":
                if (httpPort is < 1 or > 65535) return BadRequest(new { error = "HTTP 端口无效" });
                break;
            case "https":
            case "both":
                if (httpsPort is < 1 or > 65535) return BadRequest(new { error = "HTTPS 端口无效" });
                if (req.Mode == "both")
                {
                    if (httpPort is < 1 or > 65535) return BadRequest(new { error = "HTTP 端口无效" });
                    if (httpsPort == httpPort) return BadRequest(new { error = "HTTP 与 HTTPS 端口不能相同" });
                }
                if (string.IsNullOrWhiteSpace(req.Thumbprint))
                    return BadRequest(new { error = "请选择服务器证书" });
                if (PortTaken(httpsPort))
                    return BadRequest(new { error = $"端口 {httpsPort} 已被其他服务占用，请换一个端口" });
                break;
            default:
                return BadRequest(new { error = "模式无效" });
        }

        using (var ms = new MemoryStream())
        {
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteStartObject("Listeners");
                w.WriteString("Mode", req.Mode);
                w.WriteNumber("HttpPort", httpPort);
                w.WriteNumber("HttpsPort", httpsPort);
                if (!string.IsNullOrWhiteSpace(req.Thumbprint)) w.WriteString("Thumbprint", req.Thumbprint);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            System.IO.File.WriteAllText(SettingsPath, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
        }

        Task.Run(async () =>
        {
            await Task.Delay(800);
            log.LogWarning("Listener mode {Mode} applied - restarting service to re-listen", req.Mode);
            Environment.Exit(1); // SCM failure action restarts the service
        });

        var reopened = req.Mode switch
        {
            "http" => $"http://<host>:{httpPort}",
            "https" => $"https://<host>:{httpsPort}",
            _ => $"http://<host>:{httpPort} + https://<host>:{httpsPort}",
        };
        return Ok(new { message = $"监听模式已切换为 {req.Mode}，服务正在自动重启（约 5-10 秒）。重新打开: {reopened}" });
    }

    /// <summary>True when a TCP listener already occupies the port (e.g. http.sys / IIS on 443).</summary>
    private static bool PortTaken(int port)
    {
        var listeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return listeners.Any(e => e.Port == port);
    }
}
