using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace CaMgr.Api.Services;

public sealed record CsrInfo(
    string? Subject,
    string? SignatureAlgorithm,
    string? PublicKeyAlgorithm,
    int? KeySize,
    List<string> San,
    List<string> ExtendedKeyUsage,
    List<string> KeyUsage,
    string? ChallengePassword);

public sealed record CrlInfo(
    string? Issuer,
    DateTime? ThisUpdate,
    DateTime? NextUpdate,
    int RevokedCount,
    string? SignatureAlgorithm,
    List<(string Serial, DateTime? RevokedOn, int? Reason)> Entries);

/// <summary>Parses certificates, CSRs (PKCS#10) and CRLs. CRL data may be wrapped in a PKCS#7.</summary>
public static class CryptoParse
{
    public static X509Certificate2? ParseCert(byte[]? der)
    {
        if (der is null || der.Length == 0) return null;
        try { return new X509Certificate2(der); }
        catch { return null; }
    }

    public static X509Certificate2Collection ParseP7(byte[] der)
    {
        var coll = new X509Certificate2Collection();
        try { coll.Import(der); }
        catch { /* not a PKCS7 */ }
        return coll;
    }

    /// <summary>AD CS returns CRLs wrapped in a PKCS#7 SignedData; unwrap if needed.</summary>
    public static byte[] ExtractCrlFromP7(byte[] maybeP7)
    {
        // Fast path: the buffer already is a CRL.
        try
        {
            _ = new X509CrlParser().ReadCrl(maybeP7);
            return maybeP7;
        }
        catch { }

        try
        {
            var ci = Org.BouncyCastle.Asn1.Cms.ContentInfo.GetInstance(Asn1Object.FromByteArray(maybeP7));
            var sd = Org.BouncyCastle.Asn1.Cms.SignedData.GetInstance(ci.Content);
            var content = sd.EncapContentInfo?.Content;
            if (content is Asn1OctetString octets)
                return octets.GetOctets();
        }
        catch { }
        return maybeP7;
    }

    public static CrlInfo? ParseCrl(byte[]? raw)
    {
        if (raw is null || raw.Length == 0) return null;
        try
        {
            var bytes = ExtractCrlFromP7(raw);
            var crl = new X509CrlParser().ReadCrl(bytes);
            var entries = new List<(string, DateTime?, int?)>();
            IEnumerable<X509CrlEntry> revoked = crl.GetRevokedCertificates() ?? Enumerable.Empty<X509CrlEntry>();
            foreach (var e in revoked)
            {
                int? reason = null;
                try
                {
                    var ext = e.GetExtension(X509Extensions.ReasonCode);
                    if (ext is not null)
                        reason = DerEnumerated.GetInstance(ext.GetParsedValue()).Value.IntValue;
                }
                catch { }
                entries.Add((e.SerialNumber.ToString(16), e.RevocationDate, reason));
            }
            return new CrlInfo(
                crl.IssuerDN.ToString(),
                crl.ThisUpdate,
                crl.NextUpdate,
                entries.Count,
                crl.SigAlgName,
                entries);
        }
        catch
        {
            return null;
        }
    }

    public static CsrInfo? ParseCsr(byte[]? der)
    {
        if (der is null || der.Length == 0) return null;
        try
        {
            var p10 = new Pkcs10CertificationRequest(der);
            var info = p10.GetCertificationRequestInfo();

            List<string> sans = [];
            List<string> ekus = [];
            List<string> kus = [];
            string? challenge = null;

            foreach (DerSequence? attrSeq in info.Attributes.OfType<DerSequence>())
            {
                // attribute: SEQ { OID, SET { value } }
                if (attrSeq is null) continue;
                var attr = AttributeX509.GetInstance(attrSeq);
                var oid = attr.AttrType.Id;
                if (oid.Equals("1.2.840.113549.1.9.14")) // extensionRequest
                {
                    var exts = X509Extensions.GetInstance(attr.AttrValues[0]);
                    foreach (DerObjectIdentifier extOid in exts.GetExtensionOids().Cast<DerObjectIdentifier>())
                    {
                        var ext = exts.GetExtension(extOid);
                        if (extOid.Id.Equals("2.5.29.17")) // SAN
                        {
                            try
                            {
                                var names = GeneralNames.GetInstance(ext.GetParsedValue());
                                sans.AddRange(names.GetNames().Select(n => n.Name?.ToString() ?? ""));
                            }
                            catch { }
                        }
                        else if (extOid.Id.Equals("2.5.29.37")) // EKU
                        {
                            try
                            {
                                var eku = ExtendedKeyUsage.GetInstance(ext.GetParsedValue());
                                ekus.AddRange(eku.GetAllUsages().Select(o => FriendlyOid(o.Id)));
                            }
                            catch { }
                        }
                        else if (extOid.Id.Equals("2.5.29.15")) // KeyUsage
                        {
                            try
                            {
                                var bits = DerBitString.GetInstance(ext.GetParsedValue());
                                var names = new[] { "数字签名", "不可否认", "密钥加密", "数据加密", "密钥协商", "证书签名", "CRL签名", "仅加密", "仅解密" };
                                var bytes = bits.GetBytes();
                                for (int bit = 0; bit < names.Length; bit++)
                                {
                                    var b = bytes.Length > bit / 8 ? bytes[bit / 8] : (byte)0;
                                    if ((b & (0x80 >> (bit % 8))) != 0) kus.Add(names[bit]);
                                }
                            }
                            catch { }
                        }
                    }
                }
                else if (oid.Equals("1.2.840.113549.1.9.7")) // challengePassword
                {
                    try { challenge = ((DerUtf8String?)((DerSet)attr.AttrValues[0])[0])?.GetString(); } catch { }
                }
            }

            var algId = info.SubjectPublicKeyInfo.AlgorithmID;
            int? keySize = null;
            try
            {
                var key = PublicKeyFactory.CreateKey(info.SubjectPublicKeyInfo);
                keySize = key switch
                {
                    Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters rk => rk.Modulus.BitLength,
                    Org.BouncyCastle.Crypto.Parameters.ECPublicKeyParameters ek => ek.Q.Curve.FieldSize,
                    Org.BouncyCastle.Crypto.Parameters.DsaPublicKeyParameters dk => dk.Parameters?.P?.BitLength,
                    _ => null
                };
            }
            catch { }

            return new CsrInfo(
                info.Subject.ToString(),
                p10.SignatureAlgorithm?.Algorithm?.Id is null ? null : SignatureAlgorithmNameHelper(p10.SignatureAlgorithm.Algorithm.Id),
                algId.Algorithm is null ? null : algId.Algorithm.Id,
                keySize,
                sans,
                ekus,
                kus,
                challenge);
        }
        catch
        {
            return null;
        }
    }

    private static string SignatureAlgorithmNameHelper(string oid) => oid switch
    {
        "1.2.840.113549.1.1.11" => "SHA256withRSA",
        "1.2.840.113549.1.1.12" => "SHA384withRSA",
        "1.2.840.113549.1.1.13" => "SHA512withRSA",
        "1.2.840.113549.1.1.5" => "SHA1withRSA",
        "1.2.840.10045.4.3.2" => "SHA256withECDSA",
        "1.2.840.10045.4.3.3" => "SHA384withECDSA",
        "1.2.840.10045.4.3.4" => "SHA512withECDSA",
        _ => oid
    };

    public static string FriendlyOid(string oid) => oid switch
    {
        "1.3.6.1.5.5.7.3.1" => "服务器身份认证 (Server Auth)",
        "1.3.6.1.5.5.7.3.2" => "客户端身份认证 (Client Auth)",
        "1.3.6.1.5.5.7.3.3" => "代码签名 (Code Signing)",
        "1.3.6.1.5.5.7.3.4" => "邮件保护 (Email Protection)",
        "1.3.6.1.5.5.7.3.8" => "时间戳 (Time Stamping)",
        "1.3.6.1.4.1.311.10.3.12" => "文档签名 (Document Signing)",
        "2.5.29.37.0" => "任意用途 (Any)",
        _ => oid
    };
}
