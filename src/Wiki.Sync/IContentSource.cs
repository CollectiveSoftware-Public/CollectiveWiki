// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>Reads the current content of a vault note by path. The engine uses it to hash observed
/// edits; later plans back it with the real vault file system.</summary>
public interface IContentSource
{
    string? Read(string path);
}
