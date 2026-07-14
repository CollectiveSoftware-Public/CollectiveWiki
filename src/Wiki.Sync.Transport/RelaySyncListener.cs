// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>The WAN serving side: registers this device on the relay and serves each incoming relayed
/// connection with mutual TLS + a <see cref="SyncServer"/>, admitting only peers <paramref name="acceptPeer"/>
/// approves. Serves one relayed connection at a time (re-registers after each), which is sufficient for v1
/// pull-based sync. The relay only ever sees the forwarded ciphertext. Mirrors <see cref="SyncListener"/> for
/// the direct-LAN tier.</summary>
public sealed class RelaySyncListener(DeviceIdentity self, SyncServer server, Func<string, bool> acceptPeer) : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);
    private readonly DeviceIdentity _self = self;
    private readonly SyncServer _server = server;
    private readonly Func<string, bool> _acceptPeer = acceptPeer;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Start registering + serving in the background. Returns once the loop is launched (not once the
    /// first registration lands — callers that must know the peer is reachable coordinate on that separately).</summary>
    public Task StartAsync(string relayHost, int relayPort, CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = RunAsync(relayHost, relayPort, _cts.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(string host, int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var stream = await RelayClient.RegisterAsync(host, port, _self.DeviceId, ct);
                using var conn = await TlsPeerConnection.AuthenticateServerAsync(stream, _self, _acceptPeer, ct);
                await _server.ServeAsync(conn.Stream, ct);
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                // relay down, handshake rejected, or peer dropped — back off, then re-register.
                try { await Task.Delay(RetryDelay, ct); } catch { return; }
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { /* loop cancelled/faulted */ }
        _cts?.Dispose();
    }
}
