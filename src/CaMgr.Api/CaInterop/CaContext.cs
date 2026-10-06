using System.Runtime.InteropServices;

namespace CaMgr.Api.CaInterop;

/// <summary>
/// Resolves the CA config string and hands out COM objects.
/// All COM objects are created per call (they are cheap local proxies) and rely on
/// the CLR release at GC; callers that need deterministic lifetime use Marshal.FinalReleaseComObject.
/// </summary>
public sealed class CaContext
{
    private readonly object _sync = new();
    private string? _config;

    public string Config
    {
        get
        {
            lock (_sync)
            {
                if (_config is null)
                {
                    var getConfig = (ICertGetConfig)new CCertGetConfig();
                    _config = getConfig.GetConfig(CaConst.CC_DEFAULTCONFIG)
                        ?? throw new InvalidOperationException("ICertGetConfig.GetConfig returned an empty CA config string.");
                }
                return _config;
            }
        }
    }

    public ICertAdmin2 CreateAdmin() => (ICertAdmin2)new CCertAdmin();
    public ICertView2 CreateView() => (ICertView2)new CCertView();
    public ICertRequest2 CreateRequest() => (ICertRequest2)new CCertRequest();

    public void ResetConfig() { lock (_sync) _config = null; }
}

/// <summary>Translates COM HRESULTs into readable messages for API responses.</summary>
public static class CaError
{
    public static string Describe(Exception ex)
    {
        var target = ex.InnerException ?? ex;
        return target switch
        {
            COMException ce => $"0x{ce.HResult:X8} {ce.Message}",
            _ => target.Message
        };
    }
}
