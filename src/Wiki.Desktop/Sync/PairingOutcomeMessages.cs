// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Desktop.Sync;

/// <summary>Friendly, actionable sentences for each pairing outcome — replaces the old raw enum text
/// ("…rejected the join: WrongVault"). Pure + unit-tested; the Join dialog shows these inline.</summary>
public static class PairingOutcomeMessages
{
    public static string For(PairingOutcome outcome) => outcome switch
    {
        PairingOutcome.Accepted => "You're paired — syncing now.",
        PairingOutcome.UnknownToken => "The owner doesn't recognize this invite — ask them for a fresh one.",
        PairingOutcome.Expired => "This invite has expired — ask the owner for a fresh one.",
        PairingOutcome.AlreadyUsed => "This invite has already been used — ask the owner for a new one.",
        PairingOutcome.WrongVault => "The invite could not be read — check you pasted the whole cwiki:// link.",
        PairingOutcome.InvalidSignature => "This invite failed its security check — ask the owner for a fresh one.",
        PairingOutcome.IdentityMismatch => "This invite was issued for a different device — ask the owner to invite this one.",
        PairingOutcome.NoRoute => "This invite only works on the owner's local network. Ask them to turn on internet sync in Settings and send you a fresh invite.",
        PairingOutcome.OwnerUnreachable => "Couldn't reach the vault owner — they may be offline, or your two networks can't connect directly (for example, both behind carrier NAT). Make sure they're online with internet sync on, then try again.",
        _ => "The join could not be completed — ask the owner for a fresh invite.",
    };
}
