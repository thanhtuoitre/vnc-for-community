using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VncServer.Core.Logging;

namespace VncServer.Core.Security;

public static class CertificateManager
{
    private static readonly string CertDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThanhVNC",
        "certs");

    private static readonly string CertPath = Path.Combine(CertDir, "server.pfx");
    private const string CertPassword = "ThanhVncCertPassword_2026";

    public static X509Certificate2 GetOrCreateServerCertificate(IVncLogger? logger = null)
    {
        try
        {
            if (File.Exists(CertPath))
            {
                var loadedCert = new X509Certificate2(CertPath, CertPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                if (loadedCert.NotAfter > DateTime.UtcNow.AddDays(30))
                {
                    logger?.Info($"Loaded existing TLS server certificate. SHA-256 Fingerprint: {GetSha256Fingerprint(loadedCert)}");
                    return loadedCert;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Warn($"Could not load existing certificate, generating new one: {ex.Message}");
        }

        return GenerateAndSaveSelfSignedCertificate(logger);
    }

    public static X509Certificate2 GenerateAndSaveSelfSignedCertificate(IVncLogger? logger = null)
    {
        Directory.CreateDirectory(CertDir);

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=ThanhVNC Server", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));

        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], // Server Authentication
            critical: false));

        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

        // Export with private key to PKCS#12 (PFX)
        byte[] pfxData = cert.Export(X509ContentType.Pfx, CertPassword);
        File.WriteAllBytes(CertPath, pfxData);

        var finalCert = new X509Certificate2(pfxData, CertPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        string fingerprint = GetSha256Fingerprint(finalCert);
        logger?.Info($"Generated new self-signed TLS server certificate. SHA-256 Fingerprint: {fingerprint}");

        return finalCert;
    }

    public static string GetSha256Fingerprint(X509Certificate2 cert)
    {
        byte[] hash = SHA256.HashData(cert.RawData);
        return BitConverter.ToString(hash).Replace("-", ":");
    }
}
