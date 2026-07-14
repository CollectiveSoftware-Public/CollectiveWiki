// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Desktop;

/// <summary>Persisted desktop preferences (serialized whole via ISettingsStore).</summary>
public sealed class AppSettings
{
    public string ThemeMode { get; set; } = "System";   // System | Light | Dark
    public string? LastVaultPath { get; set; }
    public bool BacklinksVisible { get; set; }           // right panel starts collapsed

    // --- editing ---
    /// <summary>When true, edits persist automatically after a short idle and on focus loss (default true).</summary>
    public bool AutosaveEnabled { get; set; } = true;
    /// <summary>Idle delay before an autosave fires, in milliseconds.</summary>
    public int AutosaveDelayMs { get; set; } = 1500;

    // --- vault preferences (applied to the open VaultSession) ---
    public string AttachmentsFolder { get; set; } = "attachments";   // where pasted images are saved
    public string TemplatesFolder { get; set; } = "templates";       // where Insert Template looks
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }

    // --- P2P sync (Plan F2) ---
    public bool SyncEnabled { get; set; }
    public string? SyncDeviceName { get; set; }
    public string? SyncDeviceEmail { get; set; }
    public int SyncPort { get; set; } = 8767;
    public int PairingPort { get; set; } = 8768;
    public string? SyncRelayEndpoint { get; set; }
}
