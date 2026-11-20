using Microsoft.CSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;

namespace CaMgr.Api.Services;

public sealed record OcspResponderStatus
{
    public bool FeatureInstalled { get; set; }
    public string ServiceStatus { get; set; } = "";
    public List<OcspRevocationConfig> Configurations { get; set; } = [];
    public List<string> Alerts { get; set; } = [];
}

public sealed record OcspRevocationConfig
{
    public string CaName { get; set; } = "";
    public string HashAlgorithm { get; set; } = "";
    public DateTime LastModified { get; set; }
    public string ErrorInformation { get; set; } = "";
    public string SigningCertIssuer { get; set; } = "";
    public DateTime? SigningCertNotAfter { get; set; }
    public List<string> Alerts { get; set; } = [];
}

/// <summary>
/// Reads Online Responder (OCSP) configuration and health through the certadm.dll
/// OCSPAdmin COM interface (dynamic - the typelib is registered as certadm.OCSPAdmin).
/// </summary>
public sealed class OcspService(ILogger<OcspService> log)
{
    private const string ResponderServiceName = "OCSPSvc";

    public OcspResponderStatus GetStatus()
    {
        var status = new OcspResponderStatus();
        var svc = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == ResponderServiceName);
        if (svc is null)
        {
            status.ServiceStatus = "not-installed";
            status.Alerts.Add("Online Responder role not installed or responder not configured");
            return status;
        }
        try
        {
            status.ServiceStatus = svc.Status.ToString();
            if (svc.Status != ServiceControllerStatus.Running)
                status.Alerts.Add($"Responder service is {svc.Status}");
        }
        catch (Exception ex)
        {
            status.ServiceStatus = "error: " + ex.Message;
        }

        // read revocation configurations through the OCSPAdmin COM interface (dynamic)
        try
        {
            var type = Type.GetTypeFromProgID("certadm.OCSPAdmin");
            if (type is null)
            {
                status.Alerts.Add("certadm.OCSPAdmin COM class not registered");
                return status;
            }
            dynamic admin = Activator.CreateInstance(type)!;
            // GetConfiguration returns null when the responder has no revocation configurations yet
            dynamic? collection = admin.GetConfiguration(GetResponderServerName(), false);

            System.Collections.IEnumerable configs;
            try
            {
                configs = collection?.CAConfigurations ?? new List<OcspRevocationConfig>();
            }
            catch
            {
                // responder not yet configured: GetConfiguration/CAConfigurations returns null
                configs = new List<OcspRevocationConfig>();
            }

            foreach (var config in (dynamic)configs)
            {
                var rc = new OcspRevocationConfig
                {
                    CaName = (string)config.CAName,
                    HashAlgorithm = (string)config.HashAlgorithm,
                    LastModified = (DateTime)config.LastModified,
                };
                try { rc.ErrorInformation = config.ErrorInformation is null ? "" : (string)config.ErrorInformation; } catch { }
                try
                {
                    var signing = config.SigningCertificate;
                    if (signing is not null)
                    {
                        var cert = new X509Certificate2((byte[])config.SigningCertificate);
                        rc.SigningCertIssuer = cert.Issuer;
                        rc.SigningCertNotAfter = cert.NotAfter;
                        if (cert.NotAfter < DateTime.Now.AddDays(30))
                            rc.Alerts.Add($"OCSP signing certificate expires {cert.NotAfter:yyyy-MM-dd} (<= 30 days)");
                    }
                }
                catch (Exception ex) { rc.Alerts.Add("signing cert unreadable: " + ex.Message); }

                if (rc.ErrorInformation.Length > 0)
                    rc.Alerts.Add("ErrorInformation: " + rc.ErrorInformation);
                // stale data check: LastModified older than 2 days
                if (rc.LastModified < DateTime.Now.AddDays(-2))
                    rc.Alerts.Add($"revocation data last updated {rc.LastModified:yyyy-MM-dd HH:mm} (> 2 days ago)");

                if (rc.Alerts.Count > 0) status.Alerts.AddRange(rc.Alerts);
                status.Configurations.Add(rc);
            }
            if (collection is not null) Marshal.FinalReleaseComObject(collection);
            Marshal.FinalReleaseComObject(admin);
        }
        catch (Exception ex)
        {
            status.Alerts.Add("OCSPAdmin COM read failed: " + ex.Message);
            log.LogDebug(ex, "OCSPAdmin COM read failed");
        }

        if (status.Configurations.Count == 0 && status.ServiceStatus == "Running")
            status.Alerts.Add("Responder service running but no revocation configurations defined");
        return status;
    }

    /// <summary>The responder service resolves via the local DC - pass null for default.</summary>
    private static string GetResponderServerName() =>
        Environment.MachineName;
}
