// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync.Transport;

/// <summary>Answers a puller over one authenticated stream: GetIndex returns the signed index, GetContent
/// returns the requested file, until the peer sends Close or disconnects. Stateless per call; one instance
/// can serve many connections.</summary>
public sealed class SyncServer(ISyncContentProvider provider)
{
    private readonly ISyncContentProvider _provider = provider;

    public async Task ServeAsync(Stream stream, CancellationToken ct = default)
    {
        while (true)
        {
            SyncWire.MessageType type;
            byte[] payload;
            try { (type, payload) = await SyncWire.ReadFrameAsync(stream, ct); }
            catch (Exception e) when (
                e is EndOfStreamException or IOException or ObjectDisposedException or OperationCanceledException)
            {
                return; // peer hung up or the connection was torn down — nothing more to serve
            }

            switch (type)
            {
                case SyncWire.MessageType.GetIndex:
                    await SyncWire.WriteFrameAsync(stream, SyncWire.MessageType.Index,
                        SyncWire.EncodeIndex(_provider.Index()), ct);
                    break;
                case SyncWire.MessageType.GetContent:
                    var path = SyncWire.DecodePath(payload);
                    await SyncWire.WriteFrameAsync(stream, SyncWire.MessageType.Content,
                        SyncWire.EncodeContent(_provider.Content(path)), ct);
                    break;
                default:
                    return; // Close, or anything unexpected
            }
        }
    }
}
