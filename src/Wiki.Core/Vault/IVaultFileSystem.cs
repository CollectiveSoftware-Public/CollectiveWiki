// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Vault;

/// <summary>Directory-aware file access for a vault (a folder tree of '.md' files). Exists because
/// Collective.Platform's IFileSystem is not directory-aware. All paths are '/'-separated and relative
/// to the vault root. Concretes: PhysicalVaultFileSystem (host) + InMemoryVaultFileSystem (tests).</summary>
public interface IVaultFileSystem
{
    IReadOnlyList<string> EnumerateMarkdownFiles();

    /// <summary>Every file in the vault (notes AND assets like images), '/'-relative, sorted. Used to
    /// resolve <c>![[image.png]]</c> embeds to their file by name. Dot-folders are skipped (never assets).</summary>
    IReadOnlyList<string> EnumerateFiles();
    string ReadAllText(string relativePath);
    void WriteAllText(string relativePath, string content);

    /// <summary>Writes raw bytes to a vault file (used to save a pasted image asset). Creates parent
    /// folders; invalidates the enumeration cache when the file is new (so basename resolution sees it).</summary>
    void WriteAllBytes(string relativePath, byte[] data);

    void Rename(string fromRelative, string toRelative);

    /// <summary>Remove a file (used when a sync tombstone must delete the note from disk). No-op if absent.</summary>
    void Delete(string relativePath);

    /// <summary>Creates a folder (and any missing parents). Enables empty folders the note tree can show.</summary>
    void CreateDirectory(string relativePath);

    /// <summary>Every folder in the vault, '/'-relative, sorted; dot-folders skipped. Includes both
    /// explicitly-created folders and those implied by note/asset paths.</summary>
    IReadOnlyList<string> EnumerateFolders();

    /// <summary>Recursively removes a folder and everything under it. No-op if the folder is absent.</summary>
    void DeleteDirectory(string relativePath);

    /// <summary>Drop any cached directory enumeration so the next <see cref="EnumerateMarkdownFiles"/> /
    /// <see cref="EnumerateFiles"/> re-reads disk. Callers that need to observe changes made out-of-band (e.g.
    /// the sync host detecting notes edited through a different file-system instance) invalidate first.</summary>
    void Invalidate();

    bool Exists(string relativePath);
}
