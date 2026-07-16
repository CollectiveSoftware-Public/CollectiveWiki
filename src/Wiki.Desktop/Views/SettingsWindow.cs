// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Collective.Platform.Controls;

namespace Wiki.Desktop.Views;

/// <summary>The Settings dialog (Ctrl+,): theme plus vault preferences (attachments / templates folders).
/// Edits the passed <see cref="AppSettings"/> in place when the user saves; the head then persists +
/// re-applies. Code-built to match the head's other dialogs (no AXAML).</summary>
public sealed class SettingsWindow : DialogWindow
{
    private SettingsWindow() { }

    /// <summary>Shows the dialog modally over <paramref name="owner"/>. Returns true if the user saved
    /// (in which case <paramref name="settings"/> has been updated in place), false on cancel.</summary>
    public static async Task<bool> ShowAsync(Window owner, AppSettings settings)
    {
        var themeSystem = new RadioButton { Content = "System", GroupName = "theme", IsChecked = settings.ThemeMode == "System" };
        var themeLight = new RadioButton { Content = "Light", GroupName = "theme", IsChecked = settings.ThemeMode == "Light" };
        var themeDark = new RadioButton { Content = "Dark", GroupName = "theme", IsChecked = settings.ThemeMode == "Dark" };

        var autosave = new CheckBox { Content = "Autosave notes (idle + on focus loss)", IsChecked = settings.AutosaveEnabled };
        AutomationProperties.SetName(autosave, "Autosave notes");

        var autoUpdate = new CheckBox { Content = "Check for updates automatically", IsChecked = settings.UpdateCheckMode == "Automatic" };
        AutomationProperties.SetName(autoUpdate, "Check for updates automatically");

        var attachments = new TextBox { Text = settings.AttachmentsFolder, Width = 260 };
        AutomationProperties.SetName(attachments, "Attachments folder");
        var templates = new TextBox { Text = settings.TemplatesFolder, Width = 260 };
        AutomationProperties.SetName(templates, "Templates folder");

        bool saved = false;
        var ok = new Button { Content = "Save", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var dlg = new SettingsWindow
        {
            Title = "Settings", Width = 440, Height = 430, SizeToContent = SizeToContent.Manual,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Theme", FontWeight = FontWeight.Bold },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 14,
                        Children = { themeSystem, themeLight, themeDark },
                    },
                    autosave,
                    new TextBlock { Text = "Updates", FontWeight = FontWeight.Bold },
                    autoUpdate,
                    Labeled("Attachments folder (pasted images)", attachments),
                    Labeled("Templates folder", templates),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { ok, cancel },
                    },
                },
            },
        };

        ok.Click += (_, _) =>
        {
            settings.ThemeMode = themeDark.IsChecked == true ? "Dark"
                : themeLight.IsChecked == true ? "Light" : "System";
            settings.AttachmentsFolder = string.IsNullOrWhiteSpace(attachments.Text) ? "attachments" : attachments.Text.Trim();
            settings.TemplatesFolder = string.IsNullOrWhiteSpace(templates.Text) ? "templates" : templates.Text.Trim();
            settings.AutosaveEnabled = autosave.IsChecked == true;
            // Only toggle among Automatic/Manual — never silently leave Unset (that needs explicit consent).
            if (settings.UpdateCheckMode != "Unset")
                settings.UpdateCheckMode = autoUpdate.IsChecked == true ? "Automatic" : "Manual";
            saved = true;
            dlg.Close();
        };
        cancel.Click += (_, _) => dlg.Close();

        await dlg.ShowDialog(owner);
        return saved;
    }

    private static Control Labeled(string label, Control field)
        => new StackPanel { Spacing = 3, Children = { new TextBlock { Text = label }, field } };
}
