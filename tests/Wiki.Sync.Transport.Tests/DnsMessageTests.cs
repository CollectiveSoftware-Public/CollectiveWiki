// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

public class DnsMessageTests
{
    [Fact]
    public void A_ptr_query_round_trips()
    {
        var q = new DnsMessage { Id = 0, IsResponse = false };
        q.Questions.Add(new DnsQuestion(ServiceConstants.ServiceType, DnsType.PTR));

        var decoded = DnsMessage.Decode(q.Encode());

        Assert.False(decoded.IsResponse);
        Assert.Single(decoded.Questions);
        Assert.Equal(ServiceConstants.ServiceType, decoded.Questions[0].Name);
        Assert.Equal(DnsType.PTR, decoded.Questions[0].Type);
    }

    [Fact]
    public void A_service_response_round_trips_srv_txt_and_a_records()
    {
        var instance = $"dev123.{ServiceConstants.ServiceType}";
        var msg = new DnsMessage { IsResponse = true };
        msg.Answers.Add(new PtrRecord(ServiceConstants.ServiceType, 120, instance));
        msg.Answers.Add(new SrvRecord(instance, 120, 55123, "dev123.local"));
        msg.Answers.Add(new TxtRecord(instance, 120, new[] { "id=dev123" }));
        msg.Answers.Add(new ARecord(instance, 120, IPAddress.Parse("192.168.1.42")));

        var decoded = DnsMessage.Decode(msg.Encode());

        Assert.True(decoded.IsResponse);
        Assert.Equal((ushort)55123, decoded.Answers.OfType<SrvRecord>().Single().Port);
        Assert.Equal("id=dev123", decoded.Answers.OfType<TxtRecord>().Single().Strings.Single());
        Assert.Equal(IPAddress.Parse("192.168.1.42"), decoded.Answers.OfType<ARecord>().Single().Address);
        Assert.Equal(instance, decoded.Answers.OfType<PtrRecord>().Single().Target);
    }

    [Fact]
    public void Decode_follows_a_compression_pointer()
    {
        // Hand-built response: question "x.local", then an answer whose name AND rdata are pointers to it.
        // Header: id=0, flags=0x8400 (QR+AA), qd=1, an=1, ns=0, ar=0
        var bytes = new List<byte> { 0x00, 0x00, 0x84, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        int nameStart = bytes.Count; // offset 12
        bytes.AddRange(new byte[] { 1, (byte)'x', 5, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0 });
        bytes.AddRange(new byte[] { 0x00, (byte)DnsType.PTR, 0x00, 0x01 }); // qtype PTR, qclass IN
        bytes.Add(0xC0); bytes.Add((byte)nameStart);                        // answer name = pointer
        bytes.AddRange(new byte[] { 0x00, (byte)DnsType.PTR, 0x00, 0x01, 0x00, 0x00, 0x00, 0x78 }); // PTR, IN, ttl 120
        bytes.AddRange(new byte[] { 0x00, 0x02, 0xC0, (byte)nameStart });   // rdlen 2, rdata = pointer

        var decoded = DnsMessage.Decode(bytes.ToArray());

        Assert.Equal("x.local", decoded.Questions[0].Name);
        Assert.Equal("x.local", decoded.Answers.OfType<PtrRecord>().Single().Target);
    }
}
