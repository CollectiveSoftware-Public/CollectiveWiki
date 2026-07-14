// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Wiki.Desktop.Views;

/// <summary>A full-size image tab. Opens at actual resolution inside a scroller (so large images are
/// shown true-to-size and pannable); clicking toggles fit-to-window. Built in code (no AXAML).</summary>
public sealed class ImageViewer : UserControl
{
    private readonly Border _host = new();
    private readonly Bitmap? _bmp;

    public ImageViewer(string path)
    {
        try { _bmp = new Bitmap(path); } catch { _bmp = null; }
        Content = _host;
        if (_bmp is null)
        {
            _host.Child = new TextBlock
            {
                Text = "Unable to load image:\n" + path,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            return;
        }
        ShowActual();
    }

    private Image NewImage(Stretch stretch) => new()
    {
        Source = _bmp,
        Stretch = stretch,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void ShowActual()
    {
        var img = NewImage(Stretch.None);
        img.PointerPressed += (_, _) => ShowFit();
        _host.Child = new ScrollViewer
        {
            Content = img,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private void ShowFit()
    {
        var img = NewImage(Stretch.Uniform);
        img.Margin = new Avalonia.Thickness(8);
        img.PointerPressed += (_, _) => ShowActual();
        _host.Child = img;
    }
}
