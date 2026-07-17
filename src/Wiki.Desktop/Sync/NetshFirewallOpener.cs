// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Wiki.Desktop.Sync;

/// <summary>Windows firewall opener. Reads existing rules through the non-elevated Windows Firewall COM API
/// (<c>HNetCfg.FwPolicy2</c>) so a repeat launch never prompts, and only shells out to an <em>elevated</em>
/// <c>netsh advfirewall</c> when the rule is genuinely missing — the single expected UAC prompt. The rule is
/// scoped to <em>this app's exe</em> and the two sync ports (inbound TCP only), the tightest admission that
/// still lets a remote peer reach us. All failures (no exe path, elevation declined, netsh error) collapse to a
/// false/void result; the caller treats that as "not reachable" and shows the honest failure.</summary>
public sealed class NetshFirewallOpener : IFirewallOpener
{
    /// <summary>Stable rule name — the idempotency key for the COM probe and the delete.</summary>
    public const string RuleName = "CollectiveWiki Internet Sync";

    public async Task<bool> EnsureInboundAllowedAsync(int pairingPort, int syncPort, CancellationToken ct)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;   // can't scope the rule to our app → never open anything broader
        if (RuleExists()) return true;                 // already admitted (read without elevation) → no prompt
        return await RunNetshElevatedAsync(BuildAddArguments(exe, pairingPort, syncPort), ct).ConfigureAwait(false);
    }

    public Task RemoveAsync(CancellationToken ct) => RunNetshElevatedAsync(BuildDeleteArguments(), ct);

    /// <summary>The elevated <c>netsh</c> arguments that add the inbound-allow rule: scoped to our exe, TCP,
    /// the pairing + sync ports, on every profile so it works on a "Public" network too.</summary>
    public static string BuildAddArguments(string exePath, int pairingPort, int syncPort)
        => $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
           $"program=\"{exePath}\" protocol=TCP localport={pairingPort},{syncPort} profile=any enable=yes";

    /// <summary>The elevated <c>netsh</c> arguments that remove the rule (by its stable name).</summary>
    public static string BuildDeleteArguments()
        => $"advfirewall firewall delete rule name=\"{RuleName}\"";

    /// <summary>Non-elevated existence check via the Windows Firewall COM API — reading rules needs no admin,
    /// so this is what keeps ordinary launches prompt-free. Any failure returns false so the caller falls
    /// through to the (possibly-prompting) add rather than wrongly assuming the rule is present.</summary>
    private static bool RuleExists()
    {
        if (!OperatingSystem.IsWindows()) return false;   // COM firewall API is Windows-only; also scopes CA1416
        try
        {
            var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (t is null) return false;
            dynamic? policy = Activator.CreateInstance(t);
            if (policy is null) return false;
            try
            {
                foreach (dynamic rule in policy.Rules)
                {
                    string name = rule.Name;
                    if (string.Equals(name, RuleName, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            finally { Marshal.FinalReleaseComObject(policy); }
        }
        catch { return false; }
    }

    private static async Task<bool> RunNetshElevatedAsync(string arguments, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                UseShellExecute = true,               // required so Verb=runas can elevate
                Verb = "runas",                       // firewall changes need admin — triggers the one-time UAC prompt
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            return p.ExitCode == 0;
        }
        catch (Win32Exception) { return false; }          // UAC declined (ERROR_CANCELLED 1223) or shell-exec failed
        catch (OperationCanceledException) { return false; }
        catch { return false; }                            // best-effort — never crash the sync path over a firewall rule
    }
}
