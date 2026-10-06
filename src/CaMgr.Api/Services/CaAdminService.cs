using System.Runtime.InteropServices;
using CaMgr.Api.CaInterop;

namespace CaMgr.Api.Services;

/// <summary>Wraps ICertAdmin2 management operations (revoke, deny, resubmit, CRL, config). All COM work runs on the STA scheduler.</summary>
public sealed class CaAdminService(CaContext ca, StaComScheduler sta, ILogger<CaAdminService> log)
{
    public Task RevokeCertificateAsync(string serialNumber, int reason, DateTime? effectiveFrom) =>
        sta.InvokeAsync(() =>
        {
            var admin = ca.CreateAdmin();
            try
            {
                // DATE is days since 1899-12-30 (OLE automation). 0 = revocation takes effect immediately.
                double date = effectiveFrom.HasValue ? effectiveFrom.Value.ToUniversalTime().ToOADate() : 0;
                admin.RevokeCertificate(ca.Config, serialNumber, reason, date);
            }
            finally { Marshal.FinalReleaseComObject(admin); }
        });

    public Task DenyRequestAsync(int requestId) =>
        sta.InvokeAsync(() =>
        {
            var admin = ca.CreateAdmin();
            try { admin.DenyRequest(ca.Config, requestId); }
            finally { Marshal.FinalReleaseComObject(admin); }
        });

    /// <summary>Issues a pending/denied request by re-running the policy module. Returns the new disposition.</summary>
    public Task<int> ResubmitRequestAsync(int requestId) =>
        sta.InvokeAsync(() =>
        {
            var admin = ca.CreateAdmin();
            try { return admin.ResubmitRequest(ca.Config, requestId); }
            finally { Marshal.FinalReleaseComObject(admin); }
        });

    public Task PublishCrlsAsync(bool baseCrl, bool deltaCrl, DateTime? effectiveFrom = null) =>
        sta.InvokeAsync(() =>
        {
            if (!baseCrl && !deltaCrl) throw new ArgumentException("至少选择一种 CRL（基础或增量）");
            int flags = (baseCrl ? CaConst.CA_CRL_BASE : 0) | (deltaCrl ? CaConst.CA_CRL_DELTA : 0);
            var admin = ca.CreateAdmin();
            try { admin.PublishCRLs(ca.Config, effectiveFrom?.ToUniversalTime().ToOADate() ?? 0, flags); }
            finally { Marshal.FinalReleaseComObject(admin); }
        });

    public Task<object?> GetCaPropertyAsync(int propId, int propIndex, int propType) =>
        sta.InvokeAsync<object?>(() =>
        {
            var admin = ca.CreateAdmin();
            try { return admin.GetCAProperty(ca.Config, propId, propIndex, propType, 0); }
            finally { Marshal.FinalReleaseComObject(admin); }
        });

    /// <summary>Binary property as raw bytes (GetCAProperty returns base64 string for binary types).</summary>
    public async Task<byte[]?> GetCaPropertyBytesAsync(int propId, int propIndex)
    {
        var v = await GetCaPropertyAsync(propId, propIndex, CaConst.PROPTYPE_BINARY);
        return v switch
        {
            string s when s.Length > 0 => TryDecodeBase64(s),
            byte[] b => b,
            _ => null
        };
    }

    private static byte[]? TryDecodeBase64(string s)
    {
        try
        {
            var cleaned = s.Replace("-----BEGIN CERTIFICATE-----", "").Replace("-----END CERTIFICATE-----", "")
                           .Replace("-----BEGIN X509 CRL-----", "").Replace("-----END X509 CRL-----", "")
                           .Replace("\r", "").Replace("\n", "").Trim();
            return Convert.FromBase64String(cleaned);
        }
        catch (FormatException) { return null; }
    }

    public Task<object?> GetConfigEntryAsync(string node, string entry) =>
        sta.InvokeAsync<object?>(() =>
        {
            var admin = ca.CreateAdmin();
            try { return admin.GetConfigEntry(ca.Config, node, entry); }
            finally { Marshal.FinalReleaseComObject(admin); }
        });

    public Task SetConfigEntryAsync(string node, string entry, object value) =>
        sta.InvokeAsync(() =>
        {
            var admin = ca.CreateAdmin();
            try { admin.SetConfigEntry(ca.Config, node, entry, ref value); }
            finally { Marshal.FinalReleaseComObject(admin); }
        });

    /// <summary>
    /// CR_PROP_TEMPLATES returns template names/OIDs — as a multi-line single string
    /// ("name\noid\nname\noid...") or as an array, depending on server version.
    /// </summary>
    public async Task<List<(string Name, string Oid)>> GetTemplatesAsync()
    {
        var v = await GetCaPropertyAsync(CaConst.CR_PROP_TEMPLATES, 0, CaConst.PROPTYPE_STRING);
        List<string> lines = v switch
        {
            string[] a => a.ToList(),
            object[] oa => oa.Select(x => x?.ToString() ?? "").ToList(),
            string s => s.Split('\n').Select(x => x.Trim('\r')).ToList(),
            _ => [],
        };
        lines = lines.Where(l => l.Length > 0).ToList();
        var list = new List<(string, string)>();
        for (int i = 0; i + 1 < lines.Count; i += 2)
            list.Add((lines[i], lines[i + 1]));
        return list;
    }

    /// <summary>CA service health probe equivalent to certutil -ping.</summary>
    public async Task<bool> TryPingAsync()
    {
        try
        {
            _ = await GetCaPropertyAsync(CaConst.CR_PROP_CANAME, 0, CaConst.PROPTYPE_STRING);
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "CA ping failed");
            return false;
        }
    }
}
