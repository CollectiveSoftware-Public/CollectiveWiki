// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

public class CertPinningTests
{
    [Fact]
    public void A_certificate_pins_to_its_owners_device_id()
    {
        using var id = DeviceIdentity.Create();
        Assert.Equal(id.DeviceId, CertPinning.DeviceIdOf(id.TlsCertificate));
    }

    [Fact]
    public void Different_devices_pin_to_different_ids()
    {
        using var a = DeviceIdentity.Create();
        using var b = DeviceIdentity.Create();
        Assert.NotEqual(CertPinning.DeviceIdOf(a.TlsCertificate), CertPinning.DeviceIdOf(b.TlsCertificate));
    }
}
