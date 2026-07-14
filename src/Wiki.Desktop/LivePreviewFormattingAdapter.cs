// SPDX-License-Identifier: GPL-3.0-or-later
using Docs.Controls;
using Wiki.Editor;

namespace Wiki.Desktop;

/// <summary>Bridges CollectiveDocs' shared formatting toolbar (<see cref="IFormattingSurface"/>) to
/// CollectiveWiki's markdown <see cref="LivePreviewSurface"/> — every intent becomes a markdown-text
/// mutation on the surface (which the surface applies as one undo unit). The adapter persists across tab
/// switches; <see cref="Bind"/> repoints it at the active note surface. Capabilities exclude what markdown
/// can't express (colour, alignment, font, underline), so the shared toolbar hides those clusters.</summary>
internal sealed class LivePreviewFormattingAdapter : IFormattingSurface
{
    private static readonly StyleOption[] HeadingStyles =
    [
        new("normal", "Normal"),
        new("heading1", "Heading 1"),
        new("heading2", "Heading 2"),
        new("heading3", "Heading 3"),
    ];

    private LivePreviewSurface? _surface;

    public event Action? SelectionChanged;

    public FormatCaps Capabilities =>
        FormatCaps.Marks | FormatCaps.Styles | FormatCaps.Lists | FormatCaps.Indent | FormatCaps.Table;

    public IReadOnlyList<StyleOption> AvailableStyles => HeadingStyles;
    public bool CanInsertTable => _surface is not null;

    /// <summary>Repoint the toolbar at the active note surface (or null for image/graph tabs).</summary>
    public void Bind(LivePreviewSurface? surface)
    {
        if (ReferenceEquals(surface, _surface)) return;
        if (_surface is not null) _surface.SelectionChanged -= OnSurfaceChanged;
        _surface = surface;
        if (_surface is not null) _surface.SelectionChanged += OnSurfaceChanged;
        SelectionChanged?.Invoke();
    }

    private void OnSurfaceChanged() => SelectionChanged?.Invoke();

    public FormattingState EffectiveState()
    {
        if (_surface is null) return default;
        var (s, e) = _surface.SelectionSpan;
        var f = MarkdownEditing.DetectState(_surface.CurrentText, s, e);
        string styleId = f.HeadingLevel switch { 1 => "heading1", 2 => "heading2", 3 => "heading3", _ => "normal" };
        ListKind? list = f.Bullet ? ListKind.Bullet : f.Numbered ? ListKind.Number : null;
        return new FormattingState(
            f.Bold, f.Italic, Underline: false, f.Strike,
            FontSize: null, FontFamily: null, Foreground: null, Highlight: null,
            TextAlignment.Left, list, styleId);
    }

    public void ToggleMark(FormatMark mark)
    {
        switch (mark)
        {
            case FormatMark.Bold: _surface?.ToggleBold(); break;
            case FormatMark.Italic: _surface?.ToggleItalic(); break;
            case FormatMark.Strikethrough: _surface?.ToggleStrikethrough(); break;
            // Underline: no markdown form — the cap is off, so the toolbar never invokes it.
        }
    }

    public void ApplyStyle(string styleId)
        => _surface?.SetHeading(styleId switch { "heading1" => 1, "heading2" => 2, "heading3" => 3, _ => 0 });

    public void ToggleList(ListKind kind)
    {
        if (kind == ListKind.Bullet) _surface?.ToggleBulletList();
        else _surface?.ToggleNumberedList();
    }

    public void ChangeIndent(int delta) => _surface?.ChangeIndent(delta);
    public void InsertTable(int rows, int cols) => _surface?.InsertTable(rows, cols);
    public void FocusEditor() => _surface?.Focus();

    // No markdown equivalent — capability-gated off, so the shared toolbar never shows/invokes these.
    public void SetForeground(string? hex) { }
    public void SetHighlight(string? hex) { }
    public void SetAlignment(TextAlignment alignment) { }
    public void SetFontFamily(string family) { }
    public void SetFontSize(double points) { }
}
