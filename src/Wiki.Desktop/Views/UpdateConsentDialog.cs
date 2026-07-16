// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Collective.Platform.Controls;

namespace Wiki.Desktop.Views;

public enum UpdateConsentChoice { Automatic, Manual, NotNow }

/// <summary>First-run consent for update checks. NO network happens until this is answered. The copy is
/// deliberately honest: it does not claim this is the app's only network traffic (P2P sync exists).</summary>
public sealed class UpdateConsentDialog : DialogWindow
{
    private UpdateConsentDialog() { }

    public static async Task<UpdateConsentChoice> ShowAsync(Window owner)
    {
        var choice = UpdateConsentChoice.NotNow;
        var auto = new Button { Content = "Check automatically", IsDefault = true, Classes = { "accent" } };
        var manual = new Button { Content = "Only when I ask" };
        var not = new Button { Content = "Not now", IsCancel = true };
        AutomationProperties.SetName(auto, "Check automatically");
        AutomationProperties.SetName(manual, "Only when I ask");

        var dlg = new UpdateConsentDialog
        {
            Title = "Check for updates?", Width = 460, SizeToContent = SizeToContent.Height,
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Keep CollectiveWiki up to date?", FontWeight = FontWeight.Bold, FontSize = 15 },
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap, Classes = { "muted" },
                        Text = "CollectiveWiki can check GitHub for new signed releases and install them in one click. "
                             + "No account, no telemetry. This is separate from vault sync. You can change this any time in Settings.",
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { auto, manual, not },
                    },
                },
            },
        };
        auto.Click += (_, _) => { choice = UpdateConsentChoice.Automatic; dlg.Close(); };
        manual.Click += (_, _) => { choice = UpdateConsentChoice.Manual; dlg.Close(); };
        not.Click += (_, _) => { choice = UpdateConsentChoice.NotNow; dlg.Close(); };
        await dlg.ShowDialog(owner);
        return choice;
    }
}
