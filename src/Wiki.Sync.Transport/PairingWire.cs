// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>The framed handshake a joiner and owner speak to pair a device over an already-authenticated
/// (mutual-TLS) stream: the joiner sends its signed <see cref="JoinRequest"/>; the owner replies either
/// Accepted (its owner-signed <see cref="AuthorizedPeersList"/> + the content key sealed for the joiner) or
/// Rejected (the <see cref="PairingOutcome"/>). Frames use the same 1-byte type + 4-byte big-endian length
/// header as <see cref="SyncWire"/>; payloads are deterministic binary that unit-tests as pure byte round-trips.</summary>
public static class PairingWire
{
    public enum MessageType : byte { JoinRequest = 1, Accepted = 2, Rejected = 3 }

    // ---- framing (mirrors SyncWire's 5-byte header) ---------------------
    public static async Task WriteFrameAsync(Stream s, MessageType type, byte[] payload, CancellationToken ct)
    {
        var header = new byte[5];
        header[0] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), payload.Length);
        await s.WriteAsync(header, ct);
        if (payload.Length > 0) await s.WriteAsync(payload, ct);
        await s.FlushAsync(ct);
    }

    public static async Task<(MessageType Type, byte[] Payload)> ReadFrameAsync(Stream s, CancellationToken ct)
    {
        var header = await ReadExactAsync(s, 5, ct);
        var type = (MessageType)header[0];
        int len = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
        if (len < 0 || len > 64 * 1024 * 1024) throw new InvalidDataException($"frame length {len} out of range");
        var payload = len == 0 ? Array.Empty<byte>() : await ReadExactAsync(s, len, ct);
        return (type, payload);
    }

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

    // ---- JoinRequest payload --------------------------------------------
    public static byte[] EncodeJoinRequest(JoinRequest req)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(req.VaultId.ToString());
        WriteBytes(w, req.PresentedToken);
        w.Write(req.Applicant.DeviceId);
        WriteBytes(w, req.Applicant.PublicKey);
        w.Write(req.Applicant.Name);
        w.Write(req.Applicant.Email);
        WriteBytes(w, req.Signature);
        w.Flush();
        return ms.ToArray();
    }

    public static JoinRequest DecodeJoinRequest(byte[] payload)
    {
        using var ms = new MemoryStream(payload);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var vaultId = Guid.Parse(r.ReadString());
        var token = ReadBytes(r);
        var applicant = new PeerIdentity(r.ReadString(), ReadBytes(r), r.ReadString(), r.ReadString());
        var signature = ReadBytes(r);
        return new JoinRequest(vaultId, token, applicant, signature);
    }

    // ---- Accepted payload (signed roster + sealed content key) ----------
    public static byte[] EncodeAccepted(AuthorizedPeersList roster, SealedContentKey key)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(roster.OwnerDeviceId);
        w.Write(roster.Peers.Count);
        foreach (var p in roster.Peers)
        {
            w.Write(p.DeviceId);
            WriteBytes(w, p.PublicKey);
            w.Write((int)p.Role);
            w.Write(p.Name);
            w.Write(p.Email);
        }
        WriteBytes(w, roster.Signature);
        w.Write(key.Epoch);
        w.Write(key.RecipientDeviceId);
        WriteBytes(w, key.Nonce);
        WriteBytes(w, key.Ciphertext);
        WriteBytes(w, key.Tag);
        w.Flush();
        return ms.ToArray();
    }

    public static (AuthorizedPeersList Roster, SealedContentKey Key) DecodeAccepted(byte[] payload)
    {
        using var ms = new MemoryStream(payload);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var owner = r.ReadString();
        int count = r.ReadInt32();
        var peers = new List<AuthorizedPeer>(count);
        for (int i = 0; i < count; i++)
            peers.Add(new AuthorizedPeer(r.ReadString(), ReadBytes(r), (PeerRole)r.ReadInt32(), r.ReadString(), r.ReadString()));
        var signature = ReadBytes(r);
        var roster = new AuthorizedPeersList(owner, peers, signature);
        var key = new SealedContentKey(r.ReadInt32(), r.ReadString(), ReadBytes(r), ReadBytes(r), ReadBytes(r));
        return (roster, key);
    }

    // ---- Rejected payload -----------------------------------------------
    public static byte[] EncodeRejected(PairingOutcome outcome) => [(byte)outcome];
    public static PairingOutcome DecodeRejected(byte[] payload) => (PairingOutcome)payload[0];

    private static void WriteBytes(BinaryWriter w, byte[] b) { w.Write(b.Length); w.Write(b); }
    private static byte[] ReadBytes(BinaryReader r) { int n = r.ReadInt32(); return r.ReadBytes(n); }
}
