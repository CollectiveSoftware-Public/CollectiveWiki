// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Collective.Platform.Controls;
using Wiki.Desktop.Update;
using Wiki.Update;

namespace Wiki.Desktop.Views;

/// <summary>Outcome of the progress dialog: the staged download on success, a cancelled flag when the
/// user stopped it (Cancel / Escape / window close), or an error message when the download failed.
/// A verification failure surfaces as <c>Staged == null</c> with no error, same as the silent path.</summary>
public sealed record UpdateProgressResult(StagedUpdate? Staged, bool Cancelled, string? Error);

/// <summary>Shows a live download-and-verify bar while an update is fetched, so "Update now" is a visible
/// operation rather than a silent pause before the app restarts. The Cancel button (and Escape / the
/// window's close box) cancels the in-flight download; the stager cleans up its partial file, and the
/// user stays on the current version.</summary>
public sealed class UpdateProgressDialog : DialogWindow
{
    private UpdateProgressDialog() { }

    public static async Task<UpdateProgressResult> RunAsync(Window owner, UpdateCoordinator coordinator, UpdateInfo info)
    {
        var status = new TextBlock
        {
            Text = $"Downloading CollectiveWiki {info.Version}…",
            FontWeight = FontWeight.Bold, FontSize = 15,
        };
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0 };
        var detail = new TextBlock { Classes = { "muted" }, Text = SizeLine(0, info.Artifact.Size) };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AutomationProperties.SetAutomationId(status, "UpdateStatus");
        AutomationProperties.SetAutomationId(bar, "UpdateProgressBar");
        AutomationProperties.SetAutomationId(detail, "UpdateDetail");
        AutomationProperties.SetAutomationId(cancel, "UpdateCancel");
        AutomationProperties.SetName(cancel, "Cancel");

        var dlg = new UpdateProgressDialog
        {
            Title = "Updating", Width = 460, SizeToContent = SizeToContent.Height,
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 12,
                Children =
                {
                    status,
                    bar,
                    detail,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel },
                    },
                },
            },
        };

        using var cts = new CancellationTokenSource();

        // Progress<T> captures this (UI) thread's synchronization context, so Report marshals back here —
        // the downloader reports the byte fraction from a background read loop and the bar updates safely.
        var progress = new Progress<double>(p =>
        {
            if (cts.IsCancellationRequested) return;
            if (p >= 0.999)                         // byte stream done — the stager now hashes and promotes
            {
                bar.IsIndeterminate = true;
                status.Text = "Verifying…";
                detail.Text = "Checking the signature and finishing up.";
            }
            else
            {
                bar.IsIndeterminate = false;
                bar.Value = p;
                detail.Text = SizeLine(p, info.Artifact.Size);
            }
        });

        // Any user-initiated close — the Cancel button, Escape (DialogWindow base), or the window's close
        // box — cancels the download. Cancelling an already-finished download is a harmless no-op.
        cancel.Click += (_, _) => dlg.Close();
        dlg.Closing += (_, _) => cts.Cancel();

        Task<StagedUpdate?>? download = null;
        dlg.Opened += async (_, _) =>
        {
            download = coordinator.DownloadAsync(info, progress, cts.Token);
            try { await download; } catch { /* result harvested after ShowDialog returns */ }
            if (dlg.IsVisible) dlg.Close();         // completed on its own → dismiss the dialog
        };

        await dlg.ShowDialog(owner);

        // ShowDialog has returned, so the dialog is closed. Await the same task to settle the outcome:
        // on a user close the token is cancelled, so this unwinds to OperationCanceledException.
        if (download is null) return new UpdateProgressResult(null, Cancelled: true, Error: null);
        try { return new UpdateProgressResult(await download, Cancelled: false, Error: null); }
        catch (OperationCanceledException) { return new UpdateProgressResult(null, Cancelled: true, Error: null); }
        catch (Exception ex) { return new UpdateProgressResult(null, Cancelled: false, Error: ex.Message); }
    }

    private static string SizeLine(double fraction, long totalBytes)
    {
        if (totalBytes <= 0) return $"{fraction * 100:0}%";
        const double mb = 1024.0 * 1024.0;
        return $"{fraction * totalBytes / mb:0.0} MB of {totalBytes / mb:0.0} MB";
    }
}
