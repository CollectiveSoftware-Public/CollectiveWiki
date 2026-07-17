// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wiki.Desktop.Sync;

/// <summary>Admits the pairing + sync ports through the host firewall so a global-IPv6 (or port-forwarded)
/// dialer can actually reach our listeners. Enumerating an interface's IPv6 address does nothing if the OS
/// firewall drops the inbound SYN — this is the piece that makes internet reachability real. Behind an
/// interface so the head is testable and non-Windows hosts get a no-op. Best-effort throughout: a decline or
/// failure degrades to the honest "couldn't reach the owner" path, never a crash.</summary>
public interface IFirewallOpener
{
    /// <summary>Ensure inbound TCP to this app on the two sync ports is admitted by the OS firewall. Idempotent:
    /// when the rule is already present it returns without prompting; only the first add may raise a one-time
    /// elevation/UAC prompt (expected — opening an internet-facing port is a consented posture change). Returns
    /// true when the rule is in place (or no firewall action is needed on this OS), false when it could not be
    /// added (elevation declined, no firewall tool). Never throws.</summary>
    Task<bool> EnsureInboundAllowedAsync(int pairingPort, int syncPort, CancellationToken ct);

    /// <summary>Remove the rule added by <see cref="EnsureInboundAllowedAsync"/>. Idempotent; invoked only when
    /// the user turns internet sync off, so the opened port is released rather than left admitting traffic.
    /// Best-effort; never throws.</summary>
    Task RemoveAsync(CancellationToken ct);
}

/// <summary>The firewall opener for hosts with no supported firewall tool (non-Windows today). Reports success
/// so callers treat "nothing to do" the same as "admitted".</summary>
public sealed class NoOpFirewallOpener : IFirewallOpener
{
    public Task<bool> EnsureInboundAllowedAsync(int pairingPort, int syncPort, CancellationToken ct) => Task.FromResult(true);
    public Task RemoveAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Selects the firewall opener for the running OS: netsh-backed on Windows, a no-op elsewhere.</summary>
public static class FirewallOpeners
{
    public static IFirewallOpener CreateDefault()
        => OperatingSystem.IsWindows() ? new NetshFirewallOpener() : new NoOpFirewallOpener();
}
