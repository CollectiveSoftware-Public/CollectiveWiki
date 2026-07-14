// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Collective.Platform;

namespace Wiki.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        CrashGuard.Run("CollectiveWiki", () =>
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args));

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
