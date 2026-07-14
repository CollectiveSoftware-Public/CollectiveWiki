// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Models;

/// <summary>An inbound reference: note <paramref name="FromNote"/> contains <paramref name="Link"/>
/// pointing at the note being asked about. Paths are '/'-relative to the vault root.</summary>
public sealed record Backlink(string FromNote, WikiLink Link);
