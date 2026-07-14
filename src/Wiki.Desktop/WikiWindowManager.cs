// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Collective.Platform.Abstractions;
using Collective.Platform.Controls;
using Wiki.Desktop.ViewModels;
using Wiki.Desktop.Views;

namespace Wiki.Desktop;

/// <summary>Owns the app's top-level windows so each vault opens in its own window. Holds
/// the app-global settings store + settings, tracks every open window, and routes an "open vault" request
/// to the right one via <see cref="WindowRouter"/>. Only the initial window persists/restores geometry
/// (the single "window" blob); later windows cascade from the one that spawned them, so they don't clobber
/// each other's saved position.</summary>
public sealed class WikiWindowManager
{
    private readonly ISettingsStore _store;
    private readonly AppSettings _settings;
    private readonly List<MainWindow> _windows = new();

    public WikiWindowManager(ISettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    /// <summary>Builds the first window (App wires it as <c>desktop.MainWindow</c>), restoring + persisting
    /// its geometry, and tracks it.</summary>
    public MainWindow CreateInitialWindow(MainViewModel vm, string? startupVault)
    {
        var window = Track(new MainWindow(vm, _store, _settings, this, startupVault));
        var geometry = new WindowStateService(new WindowGeometryStore(_store));
        geometry.Apply(window, geometry.Load());   // restore before Show()
        geometry.PersistOnClose(window);
        return window;
    }

    /// <summary>Routes an "open vault" request from <paramref name="requester"/>: focus a window already on
    /// that vault, fill the requester when it has no vault yet, else open a new window.</summary>
    public void OpenVault(string path, MainWindow requester)
    {
        var roots = _windows.Select(w => w.VaultRoot).ToList();
        switch (WindowRouter.Decide(path, requester.VaultRoot, roots))
        {
            case OpenAction.InPlace:
                _ = requester.OpenVaultInThisWindowAsync(path);
                break;
            case OpenAction.Focus:
                _windows.FirstOrDefault(w => WindowRouter.Same(w.VaultRoot, path))?.Activate();
                break;
            case OpenAction.NewWindow:
                OpenInNewWindow(path, requester);
                break;
        }
    }

    private void OpenInNewWindow(string path, MainWindow requester)
    {
        var window = Track(new MainWindow(new MainViewModel(), _store, _settings, this, startupVault: path));
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = requester.Width;
        window.Height = requester.Height;
        window.Position = new PixelPoint(requester.Position.X + 40, requester.Position.Y + 40);   // cascade
        window.Show();
    }

    private MainWindow Track(MainWindow window)
    {
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        return window;
    }
}
