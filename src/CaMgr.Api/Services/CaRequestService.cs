using System.Runtime.InteropServices;
using CaMgr.Api.CaInterop;

namespace CaMgr.Api.Services;

public sealed record SubmitResult(int Disposition, int RequestId, string Message, int LastStatus, string? CertificateBase64);

/// <summary>Wraps ICertRequest2 for submitting CSRs. COM work runs on the STA scheduler.</summary>
public sealed class CaRequestService(CaContext ca, StaComScheduler sta)
{
    /// <summary>
    /// Submits a CSR. Accepts PEM ("-----BEGIN ...-----") or raw base64, PKCS#10 or CMC.
    /// Template attribute drives enterprise CA issuance policy.
    /// </summary>
    public Task<SubmitResult> SubmitAsync(string request, string? templateName, string? extraAttributes = null) =>
        sta.InvokeAsync(() =>
        {
            var req = NormalizeRequest(request);

            var attrs = new List<string>();
            if (!string.IsNullOrWhiteSpace(templateName))
                attrs.Add($"CertificateTemplate:{templateName}");
            if (!string.IsNullOrWhiteSpace(extraAttributes))
                attrs.Add(extraAttributes);
            var attrStr = string.Join("\n", attrs);

            var cr = (ICertRequest2)new CCertRequest();
            try
            {
                int disposition = cr.Submit(CaConst.CR_IN_ENCODEANY | CaConst.CR_IN_BASE64, req, attrStr, ca.Config);
                var message = SafeMessage(cr);
                int lastStatus = 0, requestId = 0;
                string? cert = null;
                try { lastStatus = cr.GetLastStatus(); } catch { }
                try { requestId = cr.GetRequestId(); } catch { }
                if (disposition is CaConst.CR_DISP_ISSUED or CaConst.CR_DISP_ISSUED_OUT_OF_BAND)
                {
                    try { cert = cr.GetCertificate(CaConst.CR_OUT_BASE64); } catch { }
                }
                return new SubmitResult(disposition, requestId, message, lastStatus, cert);
            }
            finally
            {
                Marshal.FinalReleaseComObject(cr);
            }
        });

    private static string SafeMessage(ICertRequest2 cr)
    {
        try { return cr.GetDispositionMessage() ?? ""; }
        catch { return ""; }
    }

    private static string NormalizeRequest(string input)
    {
        var s = input.Trim();
        if (s.Contains("-----BEGIN"))
        {
            var body = System.Text.RegularExpressions.Regex.Replace(s, @"-----[A-Z 0-9]*-----", "");
            return body.Replace("\r", "").Replace("\n", "").Trim();
        }
        return s;
    }
}
