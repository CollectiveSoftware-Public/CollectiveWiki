// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Wiki.Sync.Transport;

public enum DnsType : ushort { A = 1, PTR = 12, TXT = 16, SRV = 33 }

public sealed record DnsQuestion(string Name, DnsType Type);

public abstract record DnsRecord(string Name, DnsType Type, uint Ttl);
public sealed record PtrRecord(string Name, uint Ttl, string Target) : DnsRecord(Name, DnsType.PTR, Ttl);
public sealed record SrvRecord(string Name, uint Ttl, ushort Port, string Target) : DnsRecord(Name, DnsType.SRV, Ttl);
public sealed record TxtRecord(string Name, uint Ttl, IReadOnlyList<string> Strings) : DnsRecord(Name, DnsType.TXT, Ttl);
public sealed record ARecord(string Name, uint Ttl, IPAddress Address) : DnsRecord(Name, DnsType.A, Ttl);

/// <summary>A hand-written, minimal DNS/DNS-SD message codec over BCL primitives (no third-party mDNS
/// library — autarky). Encodes without name compression; decodes with compression-pointer support so it
/// interoperates with standard responders. Only the record types peer discovery needs (PTR/SRV/TXT/A).</summary>
public sealed class DnsMessage
{
    public ushort Id { get; set; }
    public bool IsResponse { get; set; }
    public List<DnsQuestion> Questions { get; } = new();
    public List<DnsRecord> Answers { get; } = new();

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        Span<byte> hdr = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(hdr, Id);
        BinaryPrimitives.WriteUInt16BigEndian(hdr[2..], (ushort)(IsResponse ? 0x8400 : 0x0000)); // QR+AA for responses
        BinaryPrimitives.WriteUInt16BigEndian(hdr[4..], (ushort)Questions.Count);
        BinaryPrimitives.WriteUInt16BigEndian(hdr[6..], (ushort)Answers.Count);
        ms.Write(hdr); // NSCount + ARCount left zero

        foreach (var q in Questions)
        {
            WriteName(ms, q.Name);
            WriteU16(ms, (ushort)q.Type);
            WriteU16(ms, 1); // class IN
        }
        foreach (var a in Answers)
        {
            WriteName(ms, a.Name);
            WriteU16(ms, (ushort)a.Type);
            WriteU16(ms, 1); // class IN
            WriteU32(ms, a.Ttl);
            var rdata = EncodeRData(a);
            WriteU16(ms, (ushort)rdata.Length);
            ms.Write(rdata);
        }
        return ms.ToArray();
    }

    private static byte[] EncodeRData(DnsRecord r)
    {
        using var ms = new MemoryStream();
        switch (r)
        {
            case PtrRecord p:
                WriteName(ms, p.Target);
                break;
            case SrvRecord s:
                WriteU16(ms, 0); WriteU16(ms, 0); WriteU16(ms, s.Port); WriteName(ms, s.Target);
                break;
            case TxtRecord t:
                if (t.Strings.Count == 0) { ms.WriteByte(0); break; }
                foreach (var str in t.Strings)
                {
                    var b = Encoding.UTF8.GetBytes(str);
                    if (b.Length > 255) throw new InvalidDataException("TXT string too long");
                    ms.WriteByte((byte)b.Length); ms.Write(b);
                }
                break;
            case ARecord a:
                ms.Write(a.Address.GetAddressBytes());
                break;
        }
        return ms.ToArray();
    }

    public static DnsMessage Decode(byte[] data)
    {
        var msg = new DnsMessage
        {
            Id = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(0)),
            IsResponse = (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2)) & 0x8000) != 0,
        };
        int qd = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        int an = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6));
        int pos = 12;

        for (int i = 0; i < qd; i++)
        {
            var name = ReadName(data, ref pos);
            var type = (DnsType)BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos)); pos += 2;
            pos += 2; // class
            msg.Questions.Add(new DnsQuestion(name, type));
        }
        for (int i = 0; i < an; i++)
        {
            var name = ReadName(data, ref pos);
            var type = (DnsType)BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos)); pos += 2;
            pos += 2; // class
            uint ttl = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos)); pos += 4;
            int rdlen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos)); pos += 2;
            int rdEnd = pos + rdlen;
            switch (type)
            {
                case DnsType.PTR:
                    msg.Answers.Add(new PtrRecord(name, ttl, ReadName(data, ref pos)));
                    break;
                case DnsType.SRV:
                    pos += 4; // priority + weight
                    ushort port = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos)); pos += 2;
                    msg.Answers.Add(new SrvRecord(name, ttl, port, ReadName(data, ref pos)));
                    break;
                case DnsType.TXT:
                    var strings = new List<string>();
                    while (pos < rdEnd)
                    {
                        int len = data[pos++];
                        strings.Add(Encoding.UTF8.GetString(data, pos, len)); pos += len;
                    }
                    msg.Answers.Add(new TxtRecord(name, ttl, strings));
                    break;
                case DnsType.A:
                    msg.Answers.Add(new ARecord(name, ttl, new IPAddress(data.AsSpan(pos, 4).ToArray())));
                    break;
            }
            pos = rdEnd; // robust against record types we skip
        }
        return msg;
    }

    // ---- name codec -----------------------------------------------------
    private static void WriteName(Stream s, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var b = Encoding.UTF8.GetBytes(label);
            if (b.Length > 63) throw new InvalidDataException("DNS label too long");
            s.WriteByte((byte)b.Length);
            s.Write(b);
        }
        s.WriteByte(0);
    }

    private static string ReadName(byte[] data, ref int pos)
    {
        var labels = new List<string>();
        bool jumped = false;
        int hops = 0;
        int cursor = pos;
        while (true)
        {
            byte len = data[cursor];
            if ((len & 0xC0) == 0xC0) // compression pointer
            {
                int ptr = ((len & 0x3F) << 8) | data[cursor + 1];
                if (!jumped) pos = cursor + 2;
                cursor = ptr;
                jumped = true;
                if (++hops > 128) throw new InvalidDataException("DNS name compression loop");
                continue;
            }
            if (len == 0)
            {
                cursor += 1;
                if (!jumped) pos = cursor;
                break;
            }
            labels.Add(Encoding.UTF8.GetString(data, cursor + 1, len));
            cursor += 1 + len;
        }
        return string.Join('.', labels);
    }

    private static void WriteU16(Stream s, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        s.Write(b);
    }

    private static void WriteU32(Stream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        s.Write(b);
    }
}
