// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>The framed request/response protocol two peers speak over an authenticated stream. Each frame is
/// a 1-byte type + 4-byte big-endian length + that many payload bytes. The index and content payloads have
/// deterministic binary encodings, decoupled from socket IO so they unit-test as pure byte round-trips.</summary>
public static class SyncWire
{
    public enum MessageType : byte { GetIndex = 1, Index = 2, GetContent = 3, Content = 4, Close = 5 }

    // ---- framing --------------------------------------------------------
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

    // ---- index payload --------------------------------------------------
    public static byte[] EncodeIndex(IReadOnlyList<SignedFileEntry> index)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(index.Count);
        foreach (var e in index)
        {
            w.Write(e.Entry.Path);
            var devices = e.Entry.Version.Devices.ToList();
            w.Write(devices.Count);
            foreach (var d in devices) { w.Write(d); w.Write(e.Entry.Version[d]); }
            w.Write(e.Entry.ContentHash);
            w.Write(e.Entry.Deleted);
            w.Write(e.Signer);
            w.Write(e.Signature.Length);
            w.Write(e.Signature);
        }
        w.Flush();
        return ms.ToArray();
    }

    public static IReadOnlyList<SignedFileEntry> DecodeIndex(byte[] payload)
    {
        using var ms = new MemoryStream(payload);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        int count = r.ReadInt32();
        var list = new List<SignedFileEntry>(count);
        for (int i = 0; i < count; i++)
        {
            var path = r.ReadString();
            int devCount = r.ReadInt32();
            var counters = new Dictionary<string, long>(devCount);
            for (int d = 0; d < devCount; d++) { var dev = r.ReadString(); counters[dev] = r.ReadInt64(); }
            var hash = r.ReadString();
            var deleted = r.ReadBoolean();
            var signer = r.ReadString();
            int sigLen = r.ReadInt32();
            var sig = r.ReadBytes(sigLen);
            list.Add(new SignedFileEntry(new FileEntry(path, new VersionVector(counters), hash, deleted), signer, sig));
        }
        return list;
    }

    // ---- content + path payloads ---------------------------------------
    public static byte[] EncodeContent(string? content)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(content is not null);
        if (content is not null) w.Write(content);
        w.Flush();
        return ms.ToArray();
    }

    public static string? DecodeContent(byte[] payload)
    {
        using var ms = new MemoryStream(payload);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        return r.ReadBoolean() ? r.ReadString() : null;
    }

    public static byte[] EncodePath(string path) => Encoding.UTF8.GetBytes(path);
    public static string DecodePath(byte[] payload) => Encoding.UTF8.GetString(payload);
}
