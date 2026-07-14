// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;
using Collective.Platform.Secrets;
using Wiki.Sync;

namespace Wiki.Sync.Host.Tests;

/// <summary>Regression for the share-vault UI freeze: KeyRingStore/EncryptedFileIdentityStore bridge the
/// async sealer synchronously, and the desktop calls them on the Avalonia UI thread (Share dialog → Add
/// collaborator → ShareVault → KeyRingStore.Save). With a secret store whose tasks complete asynchronously
/// (like the real DpapiSecretStore's file I/O), blocking a dispatcher-like thread on SealAsync deadlocked:
/// the sealer's continuation was posted back to the blocked thread. The suite's FakeSecretStore completes
/// synchronously, which is exactly why these tests must inject a yielding one.</summary>
public class SealedStoreDeadlockTests
{
    /// <summary>Every operation completes later, on the thread pool — models genuinely-async I/O.</summary>
    private sealed class YieldingSecretStore : ISecretStore
    {
        private readonly ConcurrentDictionary<string, string> _map = new();
        private Task<T> Later<T>(Func<T> f) => Task.Delay(25).ContinueWith(_ => f(), TaskScheduler.Default);
        public Task SetAsync(string key, string secret, CancellationToken ct = default)
            => Later<object?>(() => { _map[key] = secret; return null; });
        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Later(() => _map.TryGetValue(key, out var v) ? v : null);
        public Task RemoveAsync(string key, CancellationToken ct = default)
            => Later<object?>(() => { _map.TryRemove(key, out _); return null; });
    }

    /// <summary>Posted work runs on ONE pump thread, in order — the shape of Avalonia's UI thread.</summary>
    private sealed class SingleThreadedContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Cb, object? State)> _queue = new();
        public SingleThreadedContext()
        {
            var thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (cb, state) in _queue.GetConsumingEnumerable()) cb(state);
            }) { IsBackground = true };
            thread.Start();
        }
        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
        public Task Run(Action action)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ => { try { action(); tcs.SetResult(); } catch (Exception ex) { tcs.SetException(ex); } }, null);
            return tcs.Task;
        }
        public void Dispose() => _queue.CompleteAdding();
    }

    [Fact]
    public async Task Key_ring_store_round_trips_on_a_dispatcher_like_thread()
    {
        var sealer = new AtRestSealer(new YieldingSecretStore());
        var store = new InMemorySyncStore();
        var ring = VaultKeyRing.Start(new ContentKeySealer());

        using var ui = new SingleThreadedContext();
        var work = ui.Run(() =>
        {
            new KeyRingStore(store, sealer).Save(ring);   // froze the app here before the fix
            Assert.NotNull(new KeyRingStore(store, sealer).Load(new ContentKeySealer()));
        });
        var winner = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(winner == work, "sealed key-ring store deadlocked on a dispatcher-like thread");
        await work;   // surface any assertion failure from the pump thread
    }

    [Fact]
    public async Task Identity_store_round_trips_on_a_dispatcher_like_thread()
    {
        var sealer = new AtRestSealer(new YieldingSecretStore());
        var store = new InMemorySyncStore();

        using var ui = new SingleThreadedContext();
        var work = ui.Run(() =>
        {
            var identityStore = new EncryptedFileIdentityStore(store, sealer);
            identityStore.Save(new byte[] { 1, 2, 3 });
            Assert.Equal(new byte[] { 1, 2, 3 }, identityStore.Load());
        });
        var winner = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(winner == work, "sealed identity store deadlocked on a dispatcher-like thread");
        await work;   // surface any assertion failure from the pump thread
    }
}
