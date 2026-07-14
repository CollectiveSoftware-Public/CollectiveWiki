// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

public class SyncWireTests
{
    [Fact]
    public void Index_round_trips_including_a_tombstone_and_multi_device_vector()
    {
        var vec = VersionVector.Empty.Increment("dev-a").Increment("dev-a").Increment("dev-b");
        var live = new SignedFileEntry(new FileEntry("Note.md", vec, "hash123", false), "dev-a", new byte[] { 1, 2, 3, 4 });
        var gone = new SignedFileEntry(FileEntry.Tombstone("Old.md", vec), "dev-b", new byte[] { 9 });

        var decoded = SyncWire.DecodeIndex(SyncWire.EncodeIndex(new[] { live, gone }));

        Assert.Equal(2, decoded.Count);
        Assert.Equal("Note.md", decoded[0].Entry.Path);
        Assert.Equal(2L, decoded[0].Entry.Version["dev-a"]);
        Assert.Equal(1L, decoded[0].Entry.Version["dev-b"]);
        Assert.Equal("hash123", decoded[0].Entry.ContentHash);
        Assert.False(decoded[0].Entry.Deleted);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decoded[0].Signature);
        Assert.True(decoded[1].Entry.Deleted);
        Assert.Equal("dev-b", decoded[1].Signer);
    }

    [Fact]
    public void Content_round_trips_present_and_absent()
    {
        Assert.Equal("hello", SyncWire.DecodeContent(SyncWire.EncodeContent("hello")));
        Assert.Null(SyncWire.DecodeContent(SyncWire.EncodeContent(null)));
    }

    [Fact]
    public async Task Frames_round_trip_over_a_stream()
    {
        var (server, client) = Loopback.TcpPair();
        using (server) using (client)
        {
            await SyncWire.WriteFrameAsync(client, SyncWire.MessageType.GetContent, SyncWire.EncodePath("A.md"), default);
            var (type, payload) = await SyncWire.ReadFrameAsync(server, default);
            Assert.Equal(SyncWire.MessageType.GetContent, type);
            Assert.Equal("A.md", SyncWire.DecodePath(payload));
        }
    }
}
