using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CaMgr.Api.CaInterop;

namespace CaMgr.Api.Services;

public enum CertStatusFilter { All, Issued, Revoked, Pending, Failed, Denied, Expiring }

public class CertificateDto
{
    public int RequestId { get; set; }
    public string? CommonName { get; set; }
    public string? SerialNumber { get; set; }
    public string? Template { get; set; }
    public string? Requester { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }
    public int Disposition { get; set; }
    public string DispositionText { get; set; } = "";
    public string Status { get; set; } = ""; // issued | revoked | pending | denied | failed | other
    public DateTime? RevokedAt { get; set; }
    public int? RevokedReason { get; set; }
    public string? DispositionMessage { get; set; }
    public string? Thumbprint { get; set; }
    public bool HasCertificate { get; set; }
}

public sealed class CertificateDetailDto : CertificateDto
{
    public string? Subject { get; set; }
    public string? Issuer { get; set; }
    public List<string> San { get; set; } = [];
    public string? SignatureAlgorithm { get; set; }
    public string? PublicKeyAlgorithm { get; set; }
    public int? KeySize { get; set; }
    public List<string> KeyUsage { get; set; } = [];
    public List<string> EnhancedKeyUsage { get; set; } = [];
    public string? RawDerBase64 { get; set; }
    public string? Pem { get; set; }
    public CsrInfo? Csr { get; set; }
    public List<string> Chain { get; set; } = [];
}

public sealed class CertificateListResult
{
    public List<CertificateDto> Items { get; set; } = [];
    public int TotalFetched { get; set; }
    public bool Truncated { get; set; }
}

/// <summary>Builds certificate/request DTOs from CA database rows.</summary>
public sealed class CertificateService(CaDbService db, CaAdminService admin, ILogger<CertificateService> log)
{
    private static readonly string[] ListColumns =
    [
        DbCol.RequestId, DbCol.Disposition, DbCol.CommonName, DbCol.SerialNumber, DbCol.CertificateTemplate,
        DbCol.RequesterName, DbCol.SubmittedWhen, DbCol.NotBefore, DbCol.NotAfter,
        DbCol.RevokedWhen, DbCol.RevokedReason, DbCol.DispositionMessage, DbCol.RawCertificate,
    ];

    public static string StatusOf(int disposition, DateTime? revokedAt, int? revokedReason = null) => disposition switch
    {
        // unrevoke stores reason 0xFFFFFFFF and keeps the revoked-when column populated
        CaConst.DB_DISP_ISSUED when revokedAt.HasValue
                                    && revokedReason != CaConst.CRL_REASON_UNREVOKE
                                    && revokedAt.Value <= DateTime.Now => "revoked",
        CaConst.DB_DISP_REVOKED => "revoked",
        CaConst.DB_DISP_ISSUED => "issued",
        CaConst.DB_DISP_PENDING => "pending",
        CaConst.DB_DISP_ACTIVE => "pending",
        CaConst.DB_DISP_DENIED => "denied",
        CaConst.DB_DISP_ERROR => "failed",
        CaConst.DB_DISP_CA_CERT => "cacert",
        CaConst.DB_DISP_CA_CERT_CHAIN => "cacert",
        CaConst.DB_DISP_KRA_CERT => "kracert",
        CaConst.DB_DISP_FOREIGN => "foreign",
        _ => "other",
    };

    public static string DispositionText(int disposition) => disposition switch
    {
        CaConst.DB_DISP_ACTIVE => "处理中",
        CaConst.DB_DISP_PENDING => "待处理",
        CaConst.DB_DISP_FOREIGN => "外部证书",
        CaConst.DB_DISP_CA_CERT => "CA 证书",
        CaConst.DB_DISP_CA_CERT_CHAIN => "CA 证书链",
        CaConst.DB_DISP_KRA_CERT => "KRA 证书",
        CaConst.DB_DISP_ISSUED => "已颁发",
        CaConst.DB_DISP_REVOKED => "已吊销",
        CaConst.DB_DISP_ERROR => "失败",
        CaConst.DB_DISP_DENIED => "已拒绝",
        _ => $"未知({disposition})",
    };

    public static string RevocationReasonText(int? reason) => reason switch
    {
        null => "",
        CaConst.CRL_REASON_UNSPECIFIED => "未指定",
        CaConst.CRL_REASON_KEY_COMPROMISE => "密钥泄露",
        CaConst.CRL_REASON_CA_COMPROMISE => "CA 泄露",
        CaConst.CRL_REASON_AFFILIATION_CHANGED => "从属关系变更",
        CaConst.CRL_REASON_SUPERSEDED => "已被取代",
        CaConst.CRL_REASON_CESSATION_OF_OPERATION => "停止操作",
        CaConst.CRL_REASON_CERTIFICATE_HOLD => "证书冻结",
        CaConst.CRL_REASON_REMOVE_FROM_CRL => "从 CRL 移除",
        _ => $"未知({reason})",
    };

    /// <summary>
    /// List with filters. Pagination is keyset-based on RequestID (newest first).
    /// CN keyword matching is a post-filter (contains) with a bounded scan.
    /// </summary>
    public async Task<CertificateListResult> ListAsync(
        CertStatusFilter status = CertStatusFilter.All,
        string? keyword = null,
        string? serial = null,
        string? template = null,
        DateTime? from = null, DateTime? to = null,
        int limit = 50,
        int? beforeRequestId = null,
        int expiringDays = 30)
    {
        var restrictions = new List<ViewRestriction>();

        switch (status)
        {
            case CertStatusFilter.Issued:
                restrictions.Add(new ViewRestriction(DbCol.Disposition, CaConst.CVR_SEEK_EQ, CaConst.DB_DISP_ISSUED));
                break;
            case CertStatusFilter.Revoked:
                restrictions.Add(new ViewRestriction(DbCol.Disposition, CaConst.CVR_SEEK_EQ, CaConst.DB_DISP_REVOKED));
                break;
            case CertStatusFilter.Pending:
                restrictions.Add(new ViewRestriction(DbCol.Disposition, CaConst.CVR_SEEK_EQ, CaConst.DB_DISP_PENDING));
                break;
            case CertStatusFilter.Denied:
                restrictions.Add(new ViewRestriction(DbCol.Disposition, CaConst.CVR_SEEK_EQ, CaConst.DB_DISP_DENIED));
                break;
            case CertStatusFilter.Failed:
                restrictions.Add(new ViewRestriction(DbCol.Disposition, CaConst.CVR_SEEK_EQ, CaConst.DB_DISP_ERROR));
                break;
        }

        if (!string.IsNullOrWhiteSpace(serial))
        {
            var s = serial.Trim().Replace(" ", "").Replace(":", "");
            restrictions.Add(new ViewRestriction(DbCol.SerialNumber, CaConst.CVR_SEEK_EQ, s.ToUpperInvariant()));
        }
        if (beforeRequestId.HasValue)
            restrictions.Add(new ViewRestriction(DbCol.RequestId, CaConst.CVR_SEEK_LT, beforeRequestId.Value));
        if (from.HasValue)
            restrictions.Add(new ViewRestriction(DbCol.SubmittedWhen, CaConst.CVR_SEEK_GE, from.Value));
        if (to.HasValue)
            restrictions.Add(new ViewRestriction(DbCol.SubmittedWhen, CaConst.CVR_SEEK_LE, to.Value));
        if (!string.IsNullOrWhiteSpace(template))
            restrictions.Add(new ViewRestriction(DbCol.CertificateTemplate, CaConst.CVR_SEEK_EQ, template.Trim()));

        bool kw = !string.IsNullOrWhiteSpace(keyword);
        int maxScan = Math.Max(limit * 20, 1000);
        var expiringCutoff = DateTime.Now.AddDays(expiringDays);

        Func<CaRow, bool>? post = null;
        if (kw || status == CertStatusFilter.Expiring)
        {
            post = row =>
            {
                if (kw)
                {
                    var k = keyword!.Trim();
                    var cn = CaDbService.Str(row, DbCol.CommonName) ?? "";
                    var req = CaDbService.Str(row, DbCol.RequesterName) ?? "";
                    var ser = CaDbService.Str(row, DbCol.SerialNumber) ?? "";
                    if (!cn.Contains(k, StringComparison.OrdinalIgnoreCase) &&
                        !req.Contains(k, StringComparison.OrdinalIgnoreCase) &&
                        !ser.Contains(k, StringComparison.OrdinalIgnoreCase)) return false;
                }
                if (status == CertStatusFilter.Expiring)
                {
                    var exp = CaDbService.Date(row, DbCol.NotAfter);
                    var disp = CaDbService.Int(row, DbCol.Disposition) ?? -1;
                    return exp.HasValue && exp.Value >= DateTime.Now && exp.Value <= expiringCutoff
                        && disp is CaConst.DB_DISP_ISSUED;
                }
                return true;
            };
        }

        var rows = await db.QueryAsync(ListColumns, restrictions, maxScan, postFilter: post);
        var truncated = rows.Count >= maxScan;
        var page = rows.Take(limit).Select(ToDto).ToList();
        return new CertificateListResult { Items = page, TotalFetched = rows.Count, Truncated = truncated };
    }

    public async Task<CertificateDetailDto?> DetailAsync(int requestId)
    {
        var cols = ListColumns.Append(DbCol.RawRequest).ToList();
        var row = await db.GetRowAsync(requestId, cols);
        if (row is null) return null;

        var dto = new CertificateDetailDto();
        CopyTo(row, dto);

        var der = CaDbService.Bytes(row, DbCol.RawCertificate);
        dto.HasCertificate = der is not null && der.Length > 0;
        var cert = CryptoParse.ParseCert(der);
        if (cert is not null)
        {
            dto.Subject = cert.Subject;
            dto.Issuer = cert.Issuer;
            dto.Thumbprint = cert.Thumbprint;
            dto.SignatureAlgorithm = cert.SignatureAlgorithm.FriendlyName;
            dto.KeySize = cert.GetRSAPublicKey()?.KeySize
                       ?? cert.GetECDsaPublicKey()?.KeySize
                       ?? cert.GetDSAPublicKey()?.KeySize;
            dto.PublicKeyAlgorithm = cert.PublicKey.Oid?.FriendlyName ?? cert.PublicKey.Oid?.Value;
            dto.RawDerBase64 = Convert.ToBase64String(der!);
            dto.Pem = "-----BEGIN CERTIFICATE-----\n" +
                      Convert.ToBase64String(der!, Base64FormattingOptions.InsertLineBreaks) +
                      "\n-----END CERTIFICATE-----";

            var sanExt = cert.Extensions.FirstOrDefault(e => e.Oid?.Value == "2.5.29.17");
            if (sanExt is X509SubjectAlternativeNameExtension san)
            {
                dto.San.AddRange(san.EnumerateDnsNames());
                dto.San.AddRange(san.EnumerateIPAddresses().Select(ip => ip.ToString()));
            }

            var kuExt = cert.Extensions.FirstOrDefault(e => e.Oid?.Value == "2.5.29.15");
            if (kuExt is X509KeyUsageExtension ku)
            {
                foreach (X509KeyUsageFlags flag in Enum.GetValues<X509KeyUsageFlags>())
                    if (flag != X509KeyUsageFlags.None && ku.KeyUsages.HasFlag(flag))
                        dto.KeyUsage.Add(flag.ToString());
            }
            var ekuExt = cert.Extensions.FirstOrDefault(e => e.Oid?.Value == "2.5.29.37");
            if (ekuExt is X509EnhancedKeyUsageExtension eku)
                dto.EnhancedKeyUsage.AddRange(eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o.FriendlyName ?? o.Value ?? ""));

            dto.Chain = await BuildChainSubjectsAsync();
        }

        var csrDer = CaDbService.Bytes(row, DbCol.RawRequest);
        if (csrDer is { Length: > 0 })
            dto.Csr = CryptoParse.ParseCsr(csrDer);

        return dto;
    }

    private async Task<List<string>> BuildChainSubjectsAsync()
    {
        var subjects = new List<string>();
        try
        {
            var chainBytes = await admin.GetCaPropertyBytesAsync(CaConst.CR_PROP_CASIGCERTCHAIN, 0);
            if (chainBytes is not null)
            {
                foreach (var c in CryptoParse.ParseP7(chainBytes).OfType<X509Certificate2>())
                    subjects.Add(c.Subject);
            }
        }
        catch (Exception ex) { log.LogDebug(ex, "chain build failed"); }
        return subjects;
    }

    private static CertificateDto ToDto(CaRow row) => CopyTo(row, new CertificateDto());

    private static T CopyTo<T>(CaRow row, T dto) where T : CertificateDto
    {
        var revokedAt = CaDbService.Date(row, DbCol.RevokedWhen);
        var disp = CaDbService.Int(row, DbCol.Disposition) ?? 0;

        dto.RequestId = row.RequestId;
        dto.CommonName = CaDbService.Str(row, DbCol.CommonName);
        dto.SerialNumber = CaDbService.Str(row, DbCol.SerialNumber);
        dto.Template = CaDbService.Str(row, DbCol.CertificateTemplate);
        dto.Requester = CaDbService.Str(row, DbCol.RequesterName);
        dto.SubmittedAt = CaDbService.Date(row, DbCol.SubmittedWhen);
        dto.NotBefore = CaDbService.Date(row, DbCol.NotBefore);
        dto.NotAfter = CaDbService.Date(row, DbCol.NotAfter);
        dto.RevokedAt = revokedAt;
        dto.RevokedReason = CaDbService.Int(row, DbCol.RevokedReason);
        dto.DispositionMessage = CaDbService.Str(row, DbCol.DispositionMessage);
        dto.Disposition = disp;
        dto.DispositionText = DispositionText(disp);
        dto.Status = StatusOf(disp, revokedAt, dto.RevokedReason);
        var der = CaDbService.Bytes(row, DbCol.RawCertificate);
        dto.HasCertificate = der is { Length: > 0 };
        if (dto.HasCertificate)
        {
            var cert = CryptoParse.ParseCert(der);
            dto.Thumbprint = cert?.Thumbprint;
        }
        return dto;
    }
}
