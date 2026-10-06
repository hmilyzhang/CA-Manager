using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using BigInteger = Org.BouncyCastle.Math.BigInteger;
using Org.BouncyCastle.Security;

namespace CaMgr.Api.Services;

public sealed record PgpKeyRequest(
    string Name,          // display name
    string Email,         // optional
    string Algorithm,     // RSA3072 | RSA4096 | ECC (Ed25519 + X25519)
    string Password,      // protects the private key; may be empty (strongly discouraged)
    int ValidityYears);   // 0 = no expiry

public sealed record PgpKeyResult(
    string KeyId,
    string Fingerprint,
    string UserId,
    string Algorithm,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    string PublicKeyAsc,
    string PrivateKeyAsc,
    string FileNameBase);

/// <summary>
/// Generates OpenPGP key pairs for file encryption (GnuPG-compatible, ASCII-armored).
/// Key material exists only in memory during the request; the server stores nothing
/// but an audit row. The private key is protected with the caller-supplied passphrase.
/// </summary>
public sealed class PgpService(ILogger<PgpService> log)
{
    public (PgpKeyResult? result, string error) Validate(PgpKeyRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 100)
            return (null, "请填写姓名 (Name)");
        if (req.Name.Contains('(') || req.Name.Contains(')') || req.Name.Contains('<') || req.Name.Contains('>'))
            return (null, "姓名不能包含 ( ) < > 字符");
        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            if (!req.Email.Contains('@') || req.Email.Contains(' ') || req.Email.Length > 100)
                return (null, "邮箱格式无效");
        }
        if (req.Algorithm is not ("RSA3072" or "RSA4096" or "ECC"))
            return (null, "算法无效");
        if (string.IsNullOrEmpty(req.Password))
            return (null, "请设置私钥密码（文件加密私钥必须加密保护）");
        if (req.Password.Length < 8)
            return (null, "私钥密码至少 8 位");
        if (req.ValidityYears is < 0 or > 10)
            return (null, "有效期需在 0-10 年（0=永不过期）");
        return (null, "");
    }

    public PgpKeyResult Generate(PgpKeyRequest req)
    {
        var userId = string.IsNullOrWhiteSpace(req.Email) ? req.Name : $"{req.Name} <{req.Email}>";
        var created = DateTime.UtcNow;
        var expires = req.ValidityYears > 0 ? created.AddYears(req.ValidityYears) : (DateTime?)null;
        long expirySeconds = req.ValidityYears > 0 ? (long)TimeSpan.FromDays(365 * req.ValidityYears).TotalSeconds : 0;

        var random = new SecureRandom();

        IAsymmetricCipherKeyPairGenerator masterGen;
        IAsymmetricCipherKeyPairGenerator subGen;
        PublicKeyAlgorithmTag masterAlg;
        PublicKeyAlgorithmTag subAlg;

        if (req.Algorithm == "ECC")
        {
            // modern combo: Ed25519 for signatures + X25519 for encryption (GnuPG 2.1+)
            masterAlg = PublicKeyAlgorithmTag.EdDsa;
            subAlg = PublicKeyAlgorithmTag.ECDH;
            var m = new Ed25519KeyPairGenerator();
            m.Init(new Ed25519KeyGenerationParameters(random));
            masterGen = m;
            var s = new X25519KeyPairGenerator();
            s.Init(new X25519KeyGenerationParameters(random));
            subGen = s;
        }
        else
        {
            var bits = req.Algorithm == "RSA4096" ? 4096 : 3072;
            masterAlg = PublicKeyAlgorithmTag.RsaGeneral;
            subAlg = PublicKeyAlgorithmTag.RsaGeneral;
            var m = new RsaKeyPairGenerator();
            m.Init(new RsaKeyGenerationParameters(BigInteger.ValueOf(0x10001), random, bits, 25));
            masterGen = m;
            var s = new RsaKeyPairGenerator();
            s.Init(new RsaKeyGenerationParameters(BigInteger.ValueOf(0x10001), random, bits, 25));
            subGen = s;
        }

        var masterPair = new PgpKeyPair(masterAlg, masterGen.GenerateKeyPair(), created);
        var subPair = new PgpKeyPair(subAlg, subGen.GenerateKeyPair(), created);

        // master key: sign + certify; subkey: encryption (communications + storage)
        var masterSub = new PgpSignatureSubpacketGenerator();
        masterSub.SetKeyFlags(false, PgpKeyFlags.CanSign | PgpKeyFlags.CanCertify);
        masterSub.SetPreferredSymmetricAlgorithms(false, [(int)SymmetricKeyAlgorithmTag.Aes256, (int)SymmetricKeyAlgorithmTag.Aes192]);
        masterSub.SetPreferredHashAlgorithms(false, [(int)HashAlgorithmTag.Sha512, (int)HashAlgorithmTag.Sha384, (int)HashAlgorithmTag.Sha256]);
        if (expirySeconds > 0) masterSub.SetKeyExpirationTime(false, expirySeconds);

        var subSub = new PgpSignatureSubpacketGenerator();
        subSub.SetKeyFlags(false, PgpKeyFlags.CanEncryptCommunications | PgpKeyFlags.CanEncryptStorage);
        if (expirySeconds > 0) subSub.SetKeyExpirationTime(false, expirySeconds);

        var ring = new PgpKeyRingGenerator(
            PgpSignature.PositiveCertification,
            masterPair,
            userId,
            SymmetricKeyAlgorithmTag.Aes256,
            req.Password.ToCharArray(),
            true,
            masterSub.Generate(),
            null,
            random);
        ring.AddSubKey(subPair, subSub.Generate(), null, HashAlgorithmTag.Sha512);

        var secretRing = ring.GenerateSecretKeyRing();
        var publicRing = ring.GeneratePublicKeyRing();

        var pubAsc = Armor(publicRing.GetEncoded(), "PGP PUBLIC KEY BLOCK");
        var privAsc = Armor(secretRing.GetEncoded(), "PGP PRIVATE KEY BLOCK");

        var fingerprint = Convert.ToHexString(FingerprintOf(publicRing)).ToLowerInvariant();
        var keyId = fingerprint[^16..];
        var algoText = req.Algorithm == "ECC" ? "Ed25519 + X25519" : req.Algorithm;

        log.LogInformation("PGP key generated: id={KeyId} user={UserId} algo={Algo}", keyId, userId, algoText);
        return new PgpKeyResult(keyId, FormatFp(fingerprint), userId, algoText,
            created.ToLocalTime(), expires?.ToLocalTime(), pubAsc, privAsc, Sanitize(userId));
    }

    private static byte[] masterKeyFingerprint(PgpPublicKeyRingBundle bundle)
    {
        foreach (PgpPublicKeyRing ring in bundle.GetKeyRings())
            return ring.GetPublicKey().GetFingerprint();
        throw new InvalidOperationException("empty keyring");
    }

    public static byte[] FingerprintOf(PgpPublicKeyRing ring) => ring.GetPublicKey().GetFingerprint();

    private static string FormatFp(string hex) =>
        string.Join(" ", Enumerable.Range(0, hex.Length / 4).Select(i => hex.Substring(i * 4, 4).ToUpperInvariant()));

    private static string Sanitize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s.ToLowerInvariant())
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');
        return sb.ToString().Trim('-');
    }

    private static string Armor(byte[] der, string label)
    {
        var s = Convert.ToBase64String(der);
        var sb = new System.Text.StringBuilder();
        sb.Append("-----BEGIN ").Append(label).Append("-----\n\n");
        for (int i = 0; i < s.Length; i += 64)
            sb.Append(s.Substring(i, Math.Min(64, s.Length - i))).Append('\n');
        sb.Append('=').Append(Crc24Base64(der)).Append('\n');
        sb.Append("-----END ").Append(label).Append("-----");
        return sb.ToString();
    }

    /// <summary>RFC 4880 §6.1 CRC-24 armor checksum, base64 encoded to 4 chars.</summary>
    private static string Crc24Base64(byte[] data)
    {
        uint crc = 0xB704CE;
        foreach (var b in data)
        {
            crc ^= (uint)b << 16;
            for (int i = 0; i < 8; i++)
            {
                crc <<= 1;
                if ((crc & 0x1000000) != 0) crc ^= 0x1864CFB;
            }
        }
        return Convert.ToBase64String([(byte)(crc >> 16), (byte)(crc >> 8), (byte)crc]);
    }
}
