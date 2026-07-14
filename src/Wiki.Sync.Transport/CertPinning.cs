// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography.X509Certificates;
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>Maps a presented TLS certificate to the device id it pins to — base32(SHA-256(SPKI)), identical
/// to <see cref="DeviceIdentity.DeviceId"/> for that device's own certificate. Identity is pinned by key
/// fingerprint, never PKI-trusted: chain/name/expiry validation is irrelevant; only the fingerprint matters.</summary>
public static class CertPinning
{
    public static string DeviceIdOf(X509Certificate certificate)
    {
        var c2 = certificate as X509Certificate2
            ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        return DeviceIdentity.FingerprintOf(c2.PublicKey.ExportSubjectPublicKeyInfo());
    }
}
