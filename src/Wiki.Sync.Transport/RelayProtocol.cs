// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;

namespace Wiki.Sync.Transport;

/// <summary>The role this client declares in its opening HELLO frame to a CollectiveRelay.</summary>
public enum RelayRole : byte { Register = 1, Connect = 2 }

/// <summary>The client half of the CollectiveRelay wire — byte-identical to the relay repo's `RelayProtocol`.
/// A connection opens with one HELLO frame — <c>[role:1][idLen:2 big-endian][deviceId UTF-8]</c> — and the
/// relay replies with one ACK byte (1 = ok, 0 = rejected). After an ok ACK the stream is spliced to the peer
/// and carries the end-to-end mutual-TLS session (the relay forwards ciphertext only). This is a CROSS-REPO
/// contract: `CollectiveRelay` implements the identical bytes; both pin the layout with a golden-bytes test so
/// they stay compatible without sharing a package. Do not change the wire without updating both sides.</summary>
public static class RelayProtocol
{
    public const byte AckOk = 1;
    public const byte AckRejected = 0;
    public const int MaxDeviceIdBytes = 512;

    public static async Task WriteHelloAsync(Stream s, RelayRole role, string deviceId, CancellationToken ct)
    {
        var id = Encoding.UTF8.GetBytes(deviceId);
        if (id.Length is 0 or > MaxDeviceIdBytes)
            throw new ArgumentException("device id length out of range", nameof(deviceId));
        var frame = new byte[3 + id.Length];
        frame[0] = (byte)role;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1), (ushort)id.Length);
        id.CopyTo(frame, 3);
        await s.WriteAsync(frame, ct);
        await s.FlushAsync(ct);
    }

    public static async Task<(RelayRole Role, string DeviceId)> ReadHelloAsync(Stream s, CancellationToken ct)
    {
        var head = await ReadExactAsync(s, 3, ct);
        var role = (RelayRole)head[0];
        int len = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(1));
        if (len is 0 or > MaxDeviceIdBytes) throw new InvalidDataException($"device id length {len} out of range");
        var id = await ReadExactAsync(s, len, ct);
        return (role, Encoding.UTF8.GetString(id));
    }

    public static async Task WriteAckAsync(Stream s, bool ok, CancellationToken ct)
    {
        await s.WriteAsync(new[] { ok ? AckOk : AckRejected }, ct);
        await s.FlushAsync(ct);
    }

    public static async Task<bool> ReadAckAsync(Stream s, CancellationToken ct)
        => (await ReadExactAsync(s, 1, ct))[0] == AckOk;

    private static async Task<byte[]> ReadExactAsync(Stream s, int count, CancellationToken ct)
    {
        var buf = new byte[count];
        int off = 0;
        while (off < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(off, count - off), ct);
            if (n == 0) throw new EndOfStreamException();
            off += n;
        }
        return buf;
    }
}
