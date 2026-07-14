// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Wiki.Sync;

/// <summary>A device's cryptographic identity: a self-signed P-256 (ECDSA) X.509 certificate. The
/// device id is the base32 SHA-256 fingerprint of the certificate's SubjectPublicKeyInfo — the value a
/// peer pins and authorizes. The private key signs this device's changes; <see cref="Verify"/> checks a
/// signature against a raw public key (SPKI), which is how a peer verifies an author it only knows by
/// its published public key. BCL-only; no chain validation is performed (identity is pinned, not
/// PKI-trusted). At-rest protection of the exported key is the head's concern (Plan F).</summary>
public sealed class DeviceIdentity : IDisposable
{
    private readonly X509Certificate2 _cert;
    private readonly ECDsa _key;
    private X509Certificate2? _tlsCert;

    private DeviceIdentity(X509Certificate2 cert)
    {
        _cert = cert;
        _key = cert.GetECDsaPrivateKey()
            ?? throw new InvalidOperationException("certificate has no EC private key");
        PublicKey = _key.ExportSubjectPublicKeyInfo();
        DeviceId = FingerprintOf(PublicKey);
    }

    /// <summary>The pinning identity: base32(SHA-256(SubjectPublicKeyInfo)).</summary>
    public string DeviceId { get; }

    /// <summary>The public key as SubjectPublicKeyInfo (DER) — deterministic, round-trips through
    /// export/import unchanged, and is what peers store to verify this device's signatures.</summary>
    public byte[] PublicKey { get; }

    public static DeviceIdentity Create()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=CollectiveWiki Device", ecdsa, HashAlgorithmName.SHA256);
        // Fixed validity window (no wall-clock read): we pin the key fingerprint and never chain-validate.
        var cert = request.CreateSelfSigned(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(100));
        return new DeviceIdentity(cert);
    }

    public byte[] Sign(byte[] data) => _key.SignData(data, HashAlgorithmName.SHA256);

    /// <summary>The canonical device id derived from a public key (SubjectPublicKeyInfo):
    /// base32(SHA-256(spki)). Certificate pinning recomputes this from a presented cert and compares it to
    /// the id a peer expects — the one place the device-id algorithm lives.</summary>
    public static string FingerprintOf(byte[] publicKeySpki) => Base32.Encode(SHA256.HashData(publicKeySpki));

    /// <summary>The device certificate in an SslStream-ready form. CreateSelfSigned returns an
    /// ephemeral-key certificate that SChannel (Windows) refuses to present at a TLS handshake; round-tripping
    /// through PKCS#12 rebinds the private key so SslStream can use it. The SubjectPublicKeyInfo — and thus
    /// the device id — is unchanged. The identity owns and disposes it; callers must not dispose it.</summary>
    public X509Certificate2 TlsCertificate =>
        _tlsCert ??= X509CertificateLoader.LoadPkcs12(
            _cert.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.Exportable);

    /// <summary>Static ECDH: derive the raw shared secret between this device's private key and another
    /// device's public key (SPKI) — the input to the KEK that seals the vault content key. The same P-256
    /// key backs both ECDSA signing and ECDH key-agreement (spec §6/§12). Symmetric: this device and the
    /// other derive the identical secret from the mirrored key pair.</summary>
    public byte[] DeriveSharedSecret(byte[] otherPublicKeySpki)
    {
        using var mine = ECDiffieHellman.Create();
        mine.ImportParameters(_key.ExportParameters(includePrivateParameters: true));
        using var other = ECDiffieHellman.Create();
        other.ImportSubjectPublicKeyInfo(otherPublicKeySpki, out _);
        return mine.DeriveRawSecretAgreement(other.PublicKey);
    }

    public static bool Verify(byte[] publicKeySpki, byte[] data, byte[] signature)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
        return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
    }

    public byte[] Export(string? password = null) => _cert.Export(X509ContentType.Pfx, password);

    public static DeviceIdentity Import(byte[] pfx, string? password = null)
        => new(X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.Exportable));

    public void Dispose()
    {
        _key.Dispose();
        _cert.Dispose();
        _tlsCert?.Dispose();
    }
}
