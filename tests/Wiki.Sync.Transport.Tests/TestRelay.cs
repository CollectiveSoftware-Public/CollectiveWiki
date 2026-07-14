// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

/// <summary>A faithful in-test CollectiveRelay: implements the exact <see cref="RelayProtocol"/> wire (reused
/// from the transport project) so the client's relay path is proven end-to-end without a cross-repo reference
/// to the CollectiveRelay repo (which builds and is gated on its own). Register/connect rendezvous + opaque
/// byte splice, same as the production RelayServer.</summary>
internal sealed class TestRelay : IDisposable
{
    private readonly ConcurrentDictionary<string, TcpClient> _registered = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _registrations = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public IPEndPoint Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_listener, _cts.Token);
        return (IPEndPoint)_listener.LocalEndpoint;
    }

    public Task WaitForRegistrationAsync(string deviceId)
        => _registrations.GetOrAdd(deviceId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch { return; }
            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var stream = client.GetStream();
            var (role, id) = await RelayProtocol.ReadHelloAsync(stream, ct);
            if (role == RelayRole.Register)
            {
                if (!_registered.TryAdd(id, client)) { await RelayProtocol.WriteAckAsync(stream, false, ct); client.Dispose(); return; }
                await RelayProtocol.WriteAckAsync(stream, true, ct);
                _registrations.GetOrAdd(id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            }
            else
            {
                if (!_registered.TryRemove(id, out var reg)) { await RelayProtocol.WriteAckAsync(stream, false, ct); client.Dispose(); return; }
                await RelayProtocol.WriteAckAsync(stream, true, ct);
                using (client) using (reg) await SpliceAsync(stream, reg.GetStream(), ct);
            }
        }
        catch { try { client.Dispose(); } catch { } }
    }

    private static async Task SpliceAsync(Stream a, Stream b, CancellationToken ct)
        => await Task.WhenAll(PumpAsync(a, b, ct), PumpAsync(b, a, ct));

    private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct)
    {
        var buf = new byte[16 * 1024];
        try { int n; while ((n = await from.ReadAsync(buf, ct)) > 0) await to.WriteAsync(buf.AsMemory(0, n), ct); }
        catch { }
        finally { try { to.Dispose(); } catch { } }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        foreach (var kv in _registered) { try { kv.Value.Dispose(); } catch { } }
        _cts?.Dispose();
    }
}
