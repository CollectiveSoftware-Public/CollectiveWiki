// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class DeviceIdentityTlsTests
{
    [Fact]
    public void FingerprintOf_the_public_key_equals_the_device_id()
    {
        using var id = DeviceIdentity.Create();
        Assert.Equal(id.DeviceId, DeviceIdentity.FingerprintOf(id.PublicKey));
    }

    [Fact]
    public void The_tls_certificate_has_a_usable_private_key_and_pins_to_this_device()
    {
        using var id = DeviceIdentity.Create();
        var cert = id.TlsCertificate;
        Assert.True(cert.HasPrivateKey);
        Assert.Equal(id.DeviceId, DeviceIdentity.FingerprintOf(cert.PublicKey.ExportSubjectPublicKeyInfo()));
    }
}
