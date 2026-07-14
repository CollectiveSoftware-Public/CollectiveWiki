// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Collective.Platform.Controls;
using Wiki.Desktop.Sync;
using Wiki.Sync;

namespace Wiki.Desktop.Views;

/// <summary>The consolidated "Join Shared Vault" dialog — one surface replacing the old five sequential
/// modals (folder picker → invite → always-shown owner-address → name → email). Code-built + delegate-driven
/// (<see cref="JoinAction"/> does the pairing), matching the head's FolderBrowserDialog/AboutDialog pattern:
/// it validates the form, shows inline progress, closes on <see cref="PairingOutcome.Accepted"/>, and
/// otherwise shows a friendly <see cref="PairingOutcomeMessages"/> line and stays open for a retry.</summary>
public sealed class JoinVaultDialog : DialogWindow
{
    public sealed record JoinForm(string Folder, string Invite, string Name, string Email, string? HostOverride);

    /// <summary>Performs the pairing for a validated form; the dialog closes on Accepted, else shows the
    /// mapped message inline.</summary>
    public Func<JoinForm, Task<PairingOutcome>>? JoinAction { get; set; }

    private readonly TextBox _folder;
    private readonly TextBox _invite;
    private readonly TextBox _name;
    private readonly TextBox _email;
    private readonly TextBox _host;
    private readonly TextBlock _status;
    private readonly Button _join;
    private bool _joined;

    public JoinVaultDialog(string? initialFolder, string? name, string? email)
    {
        Title = "Join Shared Vault";
        Width = 520; // roomier than the 440 default — deliberate

        _folder = new TextBox { Text = initialFolder ?? "" };
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += OnBrowse;
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_folder, 0);
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(_folder);
        folderRow.Children.Add(browse);

        _invite = new TextBox { PlaceholderText = "cwiki://invite/…" };
        _name = new TextBox { Text = name ?? "" };
        _email = new TextBox { Text = email ?? "" };
        _host = new TextBox { PlaceholderText = "e.g. 192.168.1.20" };

        _status = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        _join = new Button { Content = "Join", IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        _join.Click += OnJoin;
        cancel.Click += (_, _) => Close();

        foreach (var box in new[] { _folder, _invite, _name, _email })
            box.TextChanged += (_, _) => UpdateJoinEnabled();

        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var nameCol = new StackPanel { Spacing = 4, Children = { Label("Your name"), _name } };
        var emailCol = new StackPanel { Spacing = 4, Margin = new Thickness(10, 0, 0, 0), Children = { Label("Email"), _email } };
        Grid.SetColumn(nameCol, 0);
        Grid.SetColumn(emailCol, 1);
        identity.Children.Add(nameCol);
        identity.Children.Add(emailCol);

        var advanced = new Expander
        {
            Header = "Advanced — owner address override",
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Label("Owner address (host/IP)"),
                    _host,
                    new TextBlock
                    {
                        Classes = { "muted" }, TextWrapping = TextWrapping.Wrap,
                        Text = "The invite carries the owner's address; you rarely need this.",
                    },
                },
            },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children =
            {
                new StackPanel { Spacing = 4, Children = { Label("Local folder for the vault"), folderRow } },
                new StackPanel { Spacing = 4, Children = { Label("Invite code"), _invite } },
                identity,
                advanced,
                _status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { _join, cancel },
                },
            },
        };
        UpdateJoinEnabled();
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, Classes = { "muted" }, FontWeight = FontWeight.SemiBold };

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var picked = await FolderPickerDialog.ShowAsync(this, string.IsNullOrWhiteSpace(_folder.Text) ? null : _folder.Text);
        if (!string.IsNullOrEmpty(picked)) _folder.Text = picked;
    }

    private void UpdateJoinEnabled()
        => _join.IsEnabled = !string.IsNullOrWhiteSpace(_folder.Text)
            && !string.IsNullOrWhiteSpace(_invite.Text)
            && !string.IsNullOrWhiteSpace(_name.Text)
            && !string.IsNullOrWhiteSpace(_email.Text);

    private async void OnJoin(object? sender, RoutedEventArgs e)
    {
        if (JoinAction is null) return;
        _join.IsEnabled = false;
        _status.Text = "Pairing with the owner…";
        var form = new JoinForm(
            _folder.Text!.Trim(), _invite.Text!.Trim(), _name.Text!.Trim(), _email.Text!.Trim(),
            string.IsNullOrWhiteSpace(_host.Text) ? null : _host.Text!.Trim());

        PairingOutcome outcome;
        try { outcome = await JoinAction(form); }
        catch
        {
            _status.Text = "Could not reach the owner — check the address and try again.";
            UpdateJoinEnabled();
            return;
        }

        if (outcome == PairingOutcome.Accepted) { _joined = true; Close(); }
        else { _status.Text = PairingOutcomeMessages.For(outcome); UpdateJoinEnabled(); }
    }

    /// <summary>Shows the dialog modally; returns true once the vault was joined (Accepted).</summary>
    public static async Task<bool> ShowAsync(Window owner, string? initialFolder, string? name, string? email,
        Func<JoinForm, Task<PairingOutcome>> joinAction)
    {
        var dlg = new JoinVaultDialog(initialFolder, name, email) { JoinAction = joinAction };
        await dlg.ShowDialog(owner);
        return dlg._joined;
    }
}
