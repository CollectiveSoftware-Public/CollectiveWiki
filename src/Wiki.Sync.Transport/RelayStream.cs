// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net.Sockets;

namespace Wiki.Sync.Transport;

/// <summary>A relay-forwarded duplex stream that owns its TcpClient: disposing it (e.g. when the SslStream
/// layered on top is disposed with leaveInnerStreamOpen:false) closes the socket to the relay. Delegates all
/// IO to the underlying NetworkStream.</summary>
internal sealed class RelayStream(TcpClient client, NetworkStream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => inner.WriteAsync(buffer, ct);
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);

    protected override void Dispose(bool disposing)
    {
        if (disposing) { inner.Dispose(); client.Dispose(); }
        base.Dispose(disposing);
    }
}
