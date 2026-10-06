using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CaMgr.Api.Services;

public sealed record SelfServiceRequest(
    string CommonName,
    List<string> San,
    string KeyAlgorithm,   // RSA2048 | RSA4096 | ECDSA_P256
    string Template,
    string? PfxPassword = null);   // required for the viewer (approval) flow

public sealed record SelfServiceResult(
    int Disposition,
    int RequestId,
    string Message,
    string? Detail,
    string? PfxBase64,
    string? PfxPassword,
    string? CsrPem,
    string? KeyPem,
    string FileNameBase);

/// <summary>
/// Self-service certificate issuance: generates the key pair server-side, builds a PKCS#10 CSR
/// with a multi-value SAN (DNS + IP), submits to the CA, and on immediate issuance packages a
/// PKCS#12 (.pfx) with the full chain for direct use on web servers / load balancers.
/// For viewer (approval-flow) submissions the key is returned encrypted (AES-GCM with the
/// user-supplied PFX password) so it can be paired after approval without server-side plaintext storage.
/// </summary>
public sealed class SelfServiceCertService(CaRequestService submitter, ILogger<SelfServiceCertService> log)
{
    public (SelfServiceResult result, string error) Validate(SelfServiceRequest req, bool requirePfxPassword = false)
    {
        if (string.IsNullOrWhiteSpace(req.CommonName) || req.CommonName.Length > 64)
            return (null!, "请填写通用名称 (CN)");
        if (!IsValidHost(req.CommonName))
            return (null!, $"通用名称无效: {req.CommonName}");
        if (string.IsNullOrWhiteSpace(req.Template))
            return (null!, "请选择证书模板");
        if (req.KeyAlgorithm is not ("RSA2048" or "RSA4096" or "ECDSA_P256"))
            return (null!, "密钥算法无效");
        if (requirePfxPassword && (string.IsNullOrWhiteSpace(req.PfxPassword) || req.PfxPassword.Length < 8))
            return (null!, "请设置 PFX 密码（至少 8 位），审批通过后凭此密码下载 PFX");

        foreach (var s in (req.San ?? []).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var v = s.Trim();
            if (IPAddress.TryParse(v, out _)) continue;
            if (!IsValidHost(v)) return (null!, $"SAN 值无效: {v}（须为域名或 IP）");
        }
        return (null!, "");
    }

    private static bool IsValidHost(string s)
    {
        if (s.Length > 253 || s.Length == 0) return false;
        var labels = s.TrimEnd('.').Split('.');
        return labels.Length > 0 && labels.All(l =>
            l.Length >= 1 && l.Length <= 63 &&
            l.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '*') &&
            !l.StartsWith('-') && !l.EndsWith('-'));
    }

    /// <summary>Generates the key pair + CSR. Returns the CSR DER and the PKCS8 private key.</summary>
    public (byte[] CsrDer, byte[] Pkcs8Key) BuildCsr(SelfServiceRequest req)
    {
        using RSA? rsa = req.KeyAlgorithm switch
        {
            "RSA2048" => RSA.Create(2048),
            "RSA4096" => RSA.Create(4096),
            _ => null,
        };
        using ECDsa? ecdsa = req.KeyAlgorithm == "ECDSA_P256" ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : null;

        var subject = new X500DistinguishedName($"CN={req.CommonName}", X500DistinguishedNameFlags.UseCommas);
        CertificateRequest certReq = rsa is not null
            ? new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            : new CertificateRequest(subject, ecdsa!, HashAlgorithmName.SHA256);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(req.CommonName);
        foreach (var s in (req.San ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IPAddress.TryParse(s, out var ip)) sanBuilder.AddIpAddress(ip);
            else sanBuilder.AddDnsName(s);
        }
        certReq.CertificateExtensions.Add(sanBuilder.Build());
        certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        certReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        // Server Auth + Client Auth EKU — standard for web servers / load balancers
        var eku = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2") };
        certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, false));

        var csrDer = certReq.CreateSigningRequest();
        var pkcs8 = rsa is not null ? rsa.ExportPkcs8PrivateKey() : ecdsa!.ExportPkcs8PrivateKey();
        return (csrDer, pkcs8);
    }

    /// <summary>certreq-style SAN attribute ("SAN:dns=..&ipaddress=..") honoured by the policy module.</summary>
    public static string BuildSanAttribute(string cn, IEnumerable<string> san)
    {
        var parts = new List<string> { $"dns={cn}" };
        foreach (var s in san.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IPAddress.TryParse(s, out _)) parts.Add($"ipaddress={s}");
            else parts.Add($"dns={s}");
        }
        return "SAN:" + string.Join("&", parts);
    }

    public async Task<SelfServiceResult> GenerateAndSubmitAsync(SelfServiceRequest req)
    {
        var (csrDer, pkcs8) = BuildCsr(req);
        var csrBase64 = Convert.ToBase64String(csrDer);

        // submit — SAN goes in as a CSR attribute too, so standalone/policy modules can honour it
        var sanAttribute = BuildSanAttribute(req.CommonName, req.San ?? []);
        var submit = await submitter.SubmitAsync(csrBase64, req.Template, sanAttribute);

        if (submit.Disposition is 3 or 4) // issued (or issued out of band)
        {
            var password = GeneratePassword();
            var pfx = BuildPfxFromPkcs8(pkcs8, req.KeyAlgorithm, Convert.FromBase64String(submit.CertificateBase64!), password);
            var safeName = Sanitize(req.CommonName);
            return new SelfServiceResult(submit.Disposition, submit.RequestId, "已颁发", submit.Message,
                Convert.ToBase64String(pfx), password, null, null, $"{safeName}.pfx");
        }

        // not issued immediately — hand back key + CSR so nothing is lost server-side
        var keyPem = PemWrap("PRIVATE KEY", Convert.ToBase64String(pkcs8));
        var csrPem = PemWrap("CERTIFICATE REQUEST", csrBase64);
        return new SelfServiceResult(submit.Disposition, submit.RequestId,
            submit.Disposition == 5 ? "已提交，等待审批" : "未颁发", submit.Message,
            null, null, csrPem, keyPem, Sanitize(req.CommonName));
    }

    /// <summary>Builds a PFX from a stored PKCS8 key + issued certificate, encrypted with the given password.</summary>
    public static byte[] BuildPfxFromPkcs8(byte[] pkcs8, string keyAlgorithm, byte[] certDer, string password)
    {
        var cert = new X509Certificate2(certDer);
        if (keyAlgorithm == "ECDSA_P256")
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);
            using var withKey = cert.CopyWithPrivateKey(ecdsa);
            return withKey.Export(X509ContentType.Pkcs12, password);
        }
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(pkcs8, out _);
        using var withKey2 = cert.CopyWithPrivateKey(rsa);
        return withKey2.Export(X509ContentType.Pkcs12, password);
    }

    // ---- encryption for approval-flow key escrow (AES-256-GCM, key = PBKDF2(user PFX password)) ----
    public static byte[] EncryptKeyBlob(byte[] pkcs8, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 150_000, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[pkcs8.Length];
        var tag = new byte[16];
        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        aes.Encrypt(nonce, pkcs8, cipher, tag);
        var blob = new byte[16 + 12 + 16 + cipher.Length];
        Buffer.BlockCopy(salt, 0, blob, 0, 16);
        Buffer.BlockCopy(nonce, 0, blob, 16, 12);
        Buffer.BlockCopy(tag, 0, blob, 28, 16);
        Buffer.BlockCopy(cipher, 0, blob, 44, cipher.Length);
        return blob;
    }

    public static byte[] DecryptKeyBlob(byte[] blob, string password)
    {
        var salt = blob[..16];
        var nonce = blob[16..28];
        var tag = blob[28..44];
        var cipher = blob[44..];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 150_000, HashAlgorithmName.SHA256, 32);
        var plain = new byte[cipher.Length];
        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(16);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant())
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '-');
        return sb.ToString().Trim('-');
    }

    private static string PemWrap(string label, string base64)
    {
        var sb = new StringBuilder($"-----BEGIN {label}-----\n");
        for (int i = 0; i < base64.Length; i += 64)
            sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
        sb.Append($"-----END {label}-----");
        return sb.ToString();
    }
}
