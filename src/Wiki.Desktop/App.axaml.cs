// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Collective.Platform;
using Collective.Platform.Abstractions;
using Collective.Platform.Controls;
using Wiki.Desktop.ViewModels;
using Wiki.Desktop.Views;

namespace Wiki.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var fs = new DesktopFileSystem("CollectiveWiki");
            ISettingsStore store = new FileSettingsStore(fs);
            var settings = store.LoadAsync<AppSettings>("settings").GetAwaiter().GetResult() ?? new AppSettings();

            ThemeController.Apply(settings.ThemeMode);

            var vm = new MainViewModel();
            // Resolve the startup vault but don't open it here (a large vault takes seconds to index).
            // The window opens it on a background thread once shown, so startup stays responsive.
            string? startupVault = vm.ResolveStartupVault(desktop.Args ?? []);
            if (startupVault is null && settings.LastVaultPath is { } last && System.IO.Directory.Exists(last))
                startupVault = last;

            // The window manager builds windows (each vault gets its own) and restores/persists the initial
            // window's geometry over the "window" blob; further vault windows cascade and don't persist.
            var windows = new WikiWindowManager(store, settings);
            desktop.MainWindow = windows.CreateInitialWindow(vm, startupVault);
            UtilityWindow.DefaultIcon = (desktop.MainWindow as Window)?.Icon; // fallback for ownerless utility windows
        }
        base.OnFrameworkInitializationCompleted();
    }
}
