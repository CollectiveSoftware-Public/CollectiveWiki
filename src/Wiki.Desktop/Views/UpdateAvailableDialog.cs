// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Collective.Platform.Controls;

namespace Wiki.Desktop.Views;

public enum UpdateChoice { UpdateNow, Skip, Later }

/// <summary>Notifies that a newer signed release exists and offers the D1/D2 choices. "Update now"
/// downloads, verifies, applies, and restarts; "Skip this version" is permanent for this version.</summary>
public sealed class UpdateAvailableDialog : DialogWindow
{
    private UpdateAvailableDialog() { }

    public static async Task<UpdateChoice> ShowAsync(Window owner, string version, string notesUrl)
    {
        var choice = UpdateChoice.Later;
        var update = new Button { Content = "Update now", IsDefault = true, Classes = { "accent" } };
        var skip = new Button { Content = "Skip this version" };
        var later = new Button { Content = "Later", IsCancel = true };
        AutomationProperties.SetName(update, "Update now");

        var notes = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        if (!string.IsNullOrWhiteSpace(notesUrl)) notes.Text = "Release notes: " + notesUrl;

        var dlg = new UpdateAvailableDialog
        {
            Title = "Update available", Width = 460, SizeToContent = SizeToContent.Height,
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = $"CollectiveWiki {version} is available.", FontWeight = FontWeight.Bold, FontSize = 15 },
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap, Classes = { "muted" },
                        Text = "The download is verified against the project's signing key before it is applied. "
                             + "The app will restart to finish.",
                    },
                    notes,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { update, skip, later },
                    },
                },
            },
        };
        update.Click += (_, _) => { choice = UpdateChoice.UpdateNow; dlg.Close(); };
        skip.Click += (_, _) => { choice = UpdateChoice.Skip; dlg.Close(); };
        later.Click += (_, _) => { choice = UpdateChoice.Later; dlg.Close(); };
        await dlg.ShowDialog(owner);
        return choice;
    }
}
