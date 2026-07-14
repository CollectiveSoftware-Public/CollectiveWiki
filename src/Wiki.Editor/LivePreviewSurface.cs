// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Utilities;
using Code.Core.Text;
using Wiki.Core.Editor;
using Wiki.Core.Parsing;

namespace Wiki.Editor;

/// <summary>The single-surface live-preview editor: one continuous editable
/// control where markdown markers are hidden in rendered text and revealed only on the caret's line(s),
/// selection flows across lines, front-matter shows as a Properties card, and image embeds draw inline.
/// Built in code (no AXAML). Caret/selection/input/clipboard/undo adapt CollectiveCode's proven
/// <c>EditorControl</c> over the shared <see cref="TextDocument"/>; the key simplification is that the
/// caret is always on a revealed (raw) line, so within-line caret math is exact.</summary>
public sealed class LivePreviewSurface : Control
{
    private readonly IMarkdownParser _parser = new MarkdigMarkdownParser();
    private readonly EditorModel _model;
    private TextDocument _doc = new("");
    private TextDocument? _subscribed;

    private int _caret;
    private int _anchor;
    private bool _selectionActive;
    private double _scrollY;
    private bool _dragging;

    private IReadOnlyList<EditorRow> _rows = Array.Empty<EditorRow>();
    private readonly List<RowVisual> _visuals = new();
    private double _contentHeight;
    private double _measuredWidth = -1;

    private readonly Dictionary<string, Bitmap?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _caretOn = true;
    private DispatcherTimer? _blink;

    // The front-matter Properties card starts collapsed; the user expands it by clicking
    // its header. Per-surface UI state — reset on each note load.
    private bool _propsExpanded;

    // ---- appearance ----
    private const double TopPad = 16, RowGap = 3;
    private const double BasePad = 28;             // the normal left/right text margin
    private const double FocusColumnWidth = 720;   // max content width when focus mode centres the column
    // LeftPad/RightPad are fields (not consts) so focus mode can widen them to centre a narrow column;
    // everything positions/hit-tests off them, so recomputing in one place (UpdatePadding) shifts the lot.
    private double LeftPad = BasePad, RightPad = BasePad;
    private double PropsHeaderHeight => _lineHeight + 14;   // the Properties card's clickable header band
    private const double MinBodySize = 10, MaxBodySize = 32, DefaultBodySize = 15;
    private double _bodySize = DefaultBodySize;
    private FontFamily _uiFont = new("Segoe UI, Helvetica Neue, Arial, sans-serif");
    private FontFamily _monoFont = new("Cascadia Mono, Consolas, monospace");
    private Typeface _regular, _bold, _italic, _mono;
    private double _lineHeight;

    private IBrush _bg = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private IBrush _fg = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private IBrush _muted = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private IBrush _accent = new SolidColorBrush(Color.Parse("#0284C7"));
    private IBrush _codeBrush = new SolidColorBrush(Color.FromRgb(0xB5, 0x44, 0x22));
    private IBrush _highlightBg = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xE0, 0x66));   // ==mark== behind text
    // Fenced-code syntax-token brushes (set per theme in ApplyTheme).
    private IBrush _tkKeyword = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0xFF));
    private IBrush _tkString = new SolidColorBrush(Color.FromRgb(0xA3, 0x15, 0x15));
    private IBrush _tkComment = new SolidColorBrush(Color.FromRgb(0x60, 0x8B, 0x4E));
    private IBrush _tkNumber = new SolidColorBrush(Color.FromRgb(0x09, 0x86, 0x58));
    private IBrush _tkType = new SolidColorBrush(Color.FromRgb(0x26, 0x7F, 0x99));

    // Reusable, stateless syntax highlighters, resolved by fence info-string language.
    private static readonly Code.Syntax.SyntaxHighlighterRegistry CodeRegistry = new();
    private static Code.Core.Abstractions.ISyntaxHighlighter ResolveHighlighter(string lang) =>
        CodeRegistry.ForFile(lang.ToLowerInvariant() switch
        {
            "cs" or "csharp" or "c#" => "x.cs",
            "js" or "javascript" => "x.js",
            "ts" or "typescript" => "x.ts",
            "py" or "python" => "x.py",
            "json" => "x.json",
            "c" or "h" or "cpp" or "hpp" or "cc" or "c++" => "x.cpp",
            _ => "x.txt"
        });
    private IBrush _cardBg = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));
    private IBrush _selection = new SolidColorBrush(Color.FromArgb(0x40, 0x02, 0x84, 0xC7));
    private IBrush _caretBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

    /// <summary>Raised when the user clicks a wikilink. The bool is true when a new tab was requested
    /// (Ctrl- or middle-click); false for a plain left-click (navigate the current tab).</summary>
    public event Action<string, bool>? WikiLinkActivated;
    /// <summary>Raised when the user clicks an image (the embed target) — the host opens it in a tab.</summary>
    public event Action<string>? ImageActivated;
    /// <summary>Raised on Ctrl+S.</summary>
    public event EventHandler? SaveRequested;
    /// <summary>Raised when the user asks to edit the front-matter Properties (double-click the card /
    /// command). The host opens the typed Properties editor.</summary>
    public event EventHandler? PropertiesEditRequested;
    /// <summary>Raised after any edit (so the host can mark the tab dirty).</summary>
    public event EventHandler? TextChanged;
    /// <summary>Raised whenever the caret/selection or text changes (every <see cref="Rebuild"/>), so a
    /// formatting toolbar can re-read <see cref="CurrentText"/>/<see cref="SelectionSpan"/> and reflect state.</summary>
    public event Action? SelectionChanged;

    /// <summary>Resolves an embed target (e.g. <c>pic.png</c>) to an absolute file path, or null.</summary>
    public Func<string, string?>? ImageResolver { get; set; }

    /// <summary>Saves a pasted image (bytes + extension) into the vault and returns the embed target to
    /// insert (bare file name), or null. Wired to the host; when unset, an image paste is ignored.</summary>
    public Func<byte[], string, string?>? ImageSaver { get; set; }

    /// <summary>Supplies ranked note titles for the <c>[[</c> autocomplete popup (null disables it).</summary>
    public Func<string, IReadOnlyList<string>>? LinkCandidates { get; set; }

    /// <summary>Supplies ranked tags for the <c>#</c> autocomplete popup (null disables it).</summary>
    public Func<string, IReadOnlyList<string>>? TagCandidates { get; set; }

    /// <summary>Raised for a slash command the surface can't apply itself (currently only <c>template</c>,
    /// which needs the head's template picker). Most slash commands are applied inline by the surface.</summary>
    public event Action<string>? SlashCommandInvoked;

    private bool _readMode;

    /// <summary>Reading view: every line renders rich (no revealed caret line, no caret) and edits are
    /// ignored — links, images and task checkboxes stay interactive. Toggled by the host (toolbar/Ctrl+E).</summary>
    public bool ReadMode
    {
        get => _readMode;
        set
        {
            if (_readMode == value) return;
            _readMode = value;
            Rebuild();
            InvalidateVisual();
        }
    }

    private bool _focusMode;

    /// <summary>Focus mode: centres the text in a narrow column (max <see cref="FocusColumnWidth"/>) with
    /// wide side margins, for distraction-free writing. Pure layout — content, markdown and caret math are
    /// unchanged (the column simply narrows). Per-surface, so it follows the tab. Toggled by the host.</summary>
    public bool FocusMode
    {
        get => _focusMode;
        set
        {
            if (_focusMode == value) return;
            _focusMode = value;
            Rebuild();
            InvalidateVisual();
        }
    }

    /// <summary>The current body font size in points — the host reflects/resets zoom against this.</summary>
    public double ZoomSize => _bodySize;

    /// <summary>Grow (+1) / shrink (−1) the body font, clamped to [10, 32]. Headings, code, tables and the
    /// Properties card all scale off <c>_bodySize</c>, so the whole note zooms as one. Per-surface.</summary>
    public void ChangeZoom(int delta)
    {
        double next = Math.Clamp(_bodySize + delta, MinBodySize, MaxBodySize);
        if (Math.Abs(next - _bodySize) < 0.01) return;
        _bodySize = next;
        MeasureLineHeight();
        Rebuild();
        InvalidateVisual();
    }

    /// <summary>Reset zoom to the default body size.</summary>
    public void ResetZoom()
    {
        if (Math.Abs(_bodySize - DefaultBodySize) < 0.01) return;
        _bodySize = DefaultBodySize;
        MeasureLineHeight();
        Rebuild();
        InvalidateVisual();
    }

    // Recompute the horizontal margins: focus mode centres a FocusColumnWidth-wide column (falling back to
    // the base margin when the surface is too narrow to centre); otherwise the plain base margin. Called at
    // the top of Measure so a bounds change or a focus toggle both re-derive it before layout.
    private void UpdatePadding()
    {
        if (_focusMode)
        {
            double side = Math.Max(BasePad, (Bounds.Width - FocusColumnWidth) / 2);
            LeftPad = side; RightPad = side;
        }
        else { LeftPad = BasePad; RightPad = BasePad; }
    }

    // [[ autocomplete state: the detected context + its candidates, the selected row, the QueryStart the
    // user Esc-dismissed (so the popup stays away until the context changes), and the drawn popup bounds
    // (for click hit-testing).
    private enum CompletionKind { Link, Tag, Slash }
    private CompletionKind _completionKind = CompletionKind.Link;
    private TagCompletion.Context _tagCtx;
    private SlashCompletion.Context _slashCtx;
    private IReadOnlyList<string> _slashIds = Array.Empty<string>();
    private LinkCompletion.Context? _completion;
    private IReadOnlyList<string> _completionItems = Array.Empty<string>();
    private int _completionSel;
    private int _completionDismissedAt = -1;
    private Rect _completionBox;
    private double _completionRowH;

    public LivePreviewSurface()
    {
        Focusable = true;
        // Clip drawing to our own rect: rows straddling the top edge render at a negative Y, and without
        // this the overflow paints over the formatting toolbar docked above us (matches GraphView).
        ClipToBounds = true;
        _model = new EditorModel(_parser);
        _regular = new Typeface(_uiFont);
        _bold = new Typeface(_uiFont, FontStyle.Normal, FontWeight.Bold);
        _italic = new Typeface(_uiFont, FontStyle.Italic);
        _mono = new Typeface(_monoFont);
        MeasureLineHeight();

        // Image files dragged from the OS drop in as vault assets + embeds (same pipeline as paste).
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void SetText(string text)
    {
        if (_subscribed is not null) _subscribed.Changed -= OnDocChanged;
        _doc = new TextDocument(text ?? "");
        _subscribed = _doc;
        _doc.Changed += OnDocChanged;
        _propsExpanded = false;                 // each note opens with its Properties card collapsed
        _caret = InitialCaret(text ?? "");      // start in the body, not inside the front-matter
        _anchor = _caret; _selectionActive = false; _scrollY = 0;
        Rebuild();
    }

    // Place the caret on the first body line (past any front-matter). If the caret sat at offset 0 the
    // front-matter line would reveal as raw YAML; starting in the body lets the Properties card show.
    private int InitialCaret(string text)
    {
        var fm = FrontmatterParser.Parse(text);
        if (fm is null) return 0;
        int line = fm.EndLine + 1;
        return line < _doc.LineCount ? _doc.GetLineStartOffset(line) : 0;
    }

    public string GetText() => _doc.GetText();

    public void ApplyTheme(bool dark)
    {
        if (dark)
        {
            _bg = Brush(0x1E, 0x1E, 0x1E); _fg = Brush(0xDE, 0xDE, 0xDE); _muted = Brush(0x80, 0x80, 0x80);
            _codeBrush = Brush(0xE6, 0x9A, 0x7A); _cardBg = ABrush(0x1C, 0xFF, 0xFF, 0xFF);
            _highlightBg = ABrush(0x66, 0xE0, 0xB4, 0x2A);   // muted amber reads under light text
            _caretBrush = Brush(0xDE, 0xDE, 0xDE);
            _tkKeyword = Brush(0x56, 0x9C, 0xD6); _tkString = Brush(0xCE, 0x91, 0x78); _tkComment = Brush(0x6A, 0x99, 0x55);
            _tkNumber = Brush(0xB5, 0xCE, 0xA8); _tkType = Brush(0x4E, 0xC9, 0xB0);
        }
        else
        {
            _bg = Brush(0xFF, 0xFF, 0xFF); _fg = Brush(0x1A, 0x1A, 0x1A); _muted = Brush(0x8A, 0x8A, 0x8A);
            _codeBrush = Brush(0xB5, 0x44, 0x22); _cardBg = ABrush(0x12, 0x80, 0x80, 0x80);
            _highlightBg = ABrush(0x99, 0xFF, 0xE0, 0x66);   // yellow highlighter under dark text
            _caretBrush = Brush(0x1A, 0x1A, 0x1A);
            _tkKeyword = Brush(0x00, 0x00, 0xFF); _tkString = Brush(0xA3, 0x15, 0x15); _tkComment = Brush(0x60, 0x8B, 0x4E);
            _tkNumber = Brush(0x09, 0x86, 0x58); _tkType = Brush(0x26, 0x7F, 0x99);
        }
        _selection = ABrush(0x40, 0x02, 0x84, 0xC7);
        Rebuild();
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
    private static SolidColorBrush ABrush(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));

    private void MeasureLineHeight()
    {
        var ft = new FormattedText("Mg", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _regular, _bodySize, _fg);
        _lineHeight = Math.Ceiling(ft.Height);
    }

    private void OnDocChanged(object? sender, DocumentChange e)
    {
        Rebuild();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- model / layout ----

    private double ContentWidth => Math.Max(40, Bounds.Width - LeftPad - RightPad);

    private void Rebuild()
    {
        try
        {
            string text = _doc.GetText();
            var sel = new SelectionSet(new Selection(Math.Min(_anchor, _caret), Math.Max(_anchor, _caret)));
            var plan = _readMode ? _model.ComputeReadPlan(_doc) : _model.ComputePlan(_doc, sel);
            var ast = _parser.Parse(text);
            var fm = FrontmatterParser.Parse(text);

            var texts = new List<string>(_doc.LineCount);
            var starts = new List<int>(_doc.LineCount);
            for (int i = 0; i < _doc.LineCount; i++) { texts.Add(_doc.GetLineText(i)); starts.Add(_doc.GetLineStartOffset(i)); }

            _rows = LivePreviewLayout.Build(texts, starts, plan, ast.Links, fm, ResolveHighlighter);
            Measure();
            _measuredWidth = ContentWidth;
            UpdateCompletion();
            InvalidateVisual();
            SelectionChanged?.Invoke();   // toolbar re-reads caret formatting state
        }
        catch
        {
            // never let a recompute crash the surface; leave the prior visuals in place
        }
    }

    private void Measure()
    {
        UpdatePadding();   // focus mode / bounds change may shift LeftPad-RightPad before we lay out
        _visuals.Clear();
        double width = ContentWidth;
        double y = TopPad;
        foreach (var row in _rows)
        {
            var v = new RowVisual { Row = row, Y = y };
            switch (row.Kind)
            {
                case RowKind.Rule:
                    v.Height = 18;
                    break;
                case RowKind.Image:
                    v.Image = LoadImage(row.ImageTarget!);
                    if (v.Image is { } bmp && bmp.Size.Width > 0)
                    {
                        double w, h;
                        if (row.ImageWidth is { } rw)
                        {
                            // An explicit |300 / |300x200 hint wins (still capped to the content width,
                            // scaling the height with it); no tall-image cap for a deliberate size.
                            w = Math.Min(width, rw);
                            double targetH = row.ImageHeight ?? rw * bmp.Size.Height / bmp.Size.Width;
                            h = targetH * (w / rw);
                        }
                        else
                        {
                            w = Math.Min(width, bmp.Size.Width);
                            h = w * bmp.Size.Height / bmp.Size.Width;
                            double capH = 560;
                            if (h > capH) { h = capH; w = h * bmp.Size.Width / bmp.Size.Height; }
                        }
                        v.ImageW = w; v.ImageH = h; v.Height = h + RowGap * 2;
                    }
                    else
                    {
                        v.Layout = BuildLayout("🖼 " + row.ImageTarget, MutedRun(), width);
                        v.Height = v.Layout.Height;
                    }
                    break;
                case RowKind.Properties:
                    v.Header = BuildPropertiesHeader(row, width);
                    if (_propsExpanded)
                    {
                        v.Layout = BuildPropertiesLayout(row, width);
                        v.Height = PropsHeaderHeight + v.Layout.Height + 10;
                    }
                    else
                    {
                        v.Layout = null;
                        v.Height = PropsHeaderHeight;   // collapsed: just the clickable header
                    }
                    break;
                case RowKind.Table when row.Table is { } tm:
                    BuildTableVisual(v, tm, width);
                    break;
                default:   // Text
                    v.Layout = BuildLineLayout(row, width);
                    v.Height = Math.Max(_lineHeight, v.Layout.Height);
                    break;
            }
            _visuals.Add(v);
            y += v.Height + RowGap;
        }
        _contentHeight = y + TopPad;
        ClampScroll();
    }

    private TextLayout BuildLineLayout(EditorRow row, double width)
    {
        string text = string.Concat(row.Runs.Select(r => r.Text));
        if (text.Length == 0) text = " ";   // keep a measurable line height for blank lines
        var overrides = new List<ValueSpan<TextRunProperties>>();
        int pos = 0;
        foreach (var run in row.Runs)
        {
            int len = run.Text.Length;
            if (len == 0) continue;
            overrides.Add(new ValueSpan<TextRunProperties>(pos, len, PropsFor(run, row.Revealed)));
            pos += len;
        }
        return BuildLayout(text, _regular, _bodySize, _fg, width, overrides);
    }

    // Lays out a table into a grid of cell TextLayouts + column/row edges, storing them on the RowVisual.
    private void BuildTableVisual(RowVisual v, TableModel tm, double width)
    {
        int cols = tm.Headers.Count;
        int gridRows = 1 + tm.Rows.Count;                 // header + body
        double[] colW = TableLayout.Columns(tm, s => MeasureCellWidth(s), width);

        v.ColX = new double[cols + 1];
        for (int c = 0; c < cols; c++) v.ColX[c + 1] = v.ColX[c] + colW[c];

        v.Cells = new TextLayout[gridRows, cols];
        v.RowY = new double[gridRows + 1];
        double y = 0;
        for (int r = 0; r < gridRows; r++)
        {
            double rowH = 0;
            for (int c = 0; c < cols; c++)
            {
                string cell = r == 0 ? tm.Headers[c] : (c < tm.Rows[r - 1].Count ? tm.Rows[r - 1][c] : "");
                var tf = r == 0 ? _bold : _regular;
                double inner = Math.Max(10, colW[c] - TableLayout.CellPadX * 2);
                var layout = new TextLayout(cell, tf, _bodySize, _fg, textWrapping: TextWrapping.Wrap, maxWidth: inner);
                v.Cells[r, c] = layout;
                rowH = Math.Max(rowH, layout.Height);
            }
            v.RowY[r] = y;
            y += rowH + 8;                                  // cell vertical padding
        }
        v.RowY[gridRows] = y;
        v.Height = y + RowGap;
    }

    private double MeasureCellWidth(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _regular, _bodySize, _fg);
        return Math.Min(ft.Width, 360);                     // cap a very wide cell so one column can't dominate
    }

    // The collapsed/expanded header line: a disclosure triangle + "Properties" + the entry count.
    private TextLayout BuildPropertiesHeader(EditorRow row, double width)
    {
        int count = row.Properties?.Count ?? 0;
        string tri = _propsExpanded ? "▾" : "▸";   // ▾ / ▸
        string header = tri + "  Properties" + (count > 0 ? "   (" + count + ")" : "");
        return BuildLayout(header, _bold, _bodySize * 0.92, _muted, width - 24);
    }

    private TextLayout BuildPropertiesLayout(EditorRow row, double width)
    {
        var entries = row.Properties ?? Array.Empty<KeyValuePair<string, string>>();
        var sb = new System.Text.StringBuilder();
        var overrides = new List<ValueSpan<TextRunProperties>>();
        int pos = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            string key = entries[i].Key;
            string val = entries[i].Value;
            string line = key + "   " + val + (i < entries.Count - 1 ? "\n" : "");
            overrides.Add(new ValueSpan<TextRunProperties>(pos, key.Length,
                new GenericTextRunProperties(_bold, _bodySize * 0.92, foregroundBrush: _muted)));
            int valStart = pos + key.Length + 3;
            if (val.Length > 0)
                overrides.Add(new ValueSpan<TextRunProperties>(valStart, val.Length,
                    new GenericTextRunProperties(_regular, _bodySize * 0.92, foregroundBrush: _fg)));
            sb.Append(line);
            pos += line.Length;
        }
        string text = sb.Length == 0 ? "Properties" : sb.ToString();
        return BuildLayout(text, _regular, _bodySize * 0.92, _muted, width - 24, overrides);
    }

    private TextLayout BuildLayout(string text, Typeface tf, double size, IBrush fg, double width,
        IReadOnlyList<ValueSpan<TextRunProperties>>? overrides = null)
        => new(text, tf, size, fg, textWrapping: TextWrapping.Wrap, maxWidth: Math.Max(20, width),
            textStyleOverrides: overrides);

    private TextLayout BuildLayout(string text, GenericTextRunProperties props, double width)
        => new(text, props.Typeface, props.FontRenderingEmSize, props.ForegroundBrush,
            textWrapping: TextWrapping.Wrap, maxWidth: Math.Max(20, width));

    private GenericTextRunProperties MutedRun() => new(_italic, _bodySize, foregroundBrush: _muted);

    private GenericTextRunProperties PropsFor(StyledRun run, bool revealed)
    {
        return run.Style switch
        {
            RunStyle.Heading => new GenericTextRunProperties(_bold, HeadingSize(run.HeadingLevel), foregroundBrush: _fg),
            RunStyle.Bold => new GenericTextRunProperties(_bold, _bodySize, foregroundBrush: _fg),
            RunStyle.Italic => new GenericTextRunProperties(_italic, _bodySize, foregroundBrush: _fg),
            RunStyle.Code => new GenericTextRunProperties(_mono, _bodySize * 0.95, foregroundBrush: _codeBrush),
            RunStyle.CodeKeyword => new GenericTextRunProperties(_mono, _bodySize * 0.95, foregroundBrush: _tkKeyword),
            RunStyle.CodeString => new GenericTextRunProperties(_mono, _bodySize * 0.95, foregroundBrush: _tkString),
            RunStyle.CodeComment => new GenericTextRunProperties(_italic, _bodySize * 0.95, foregroundBrush: _tkComment),
            RunStyle.CodeNumber => new GenericTextRunProperties(_mono, _bodySize * 0.95, foregroundBrush: _tkNumber),
            RunStyle.CodeType => new GenericTextRunProperties(_mono, _bodySize * 0.95, foregroundBrush: _tkType),
            RunStyle.Highlight => new GenericTextRunProperties(_regular, _bodySize, foregroundBrush: _fg, backgroundBrush: _highlightBg),
            RunStyle.Strikethrough => new GenericTextRunProperties(_regular, _bodySize, textDecorations: TextDecorations.Strikethrough, foregroundBrush: _fg),
            RunStyle.Quote => new GenericTextRunProperties(_italic, _bodySize, foregroundBrush: _muted),
            RunStyle.ListMarker => new GenericTextRunProperties(_regular, _bodySize, foregroundBrush: _muted),
            RunStyle.WikiLink or RunStyle.Link or RunStyle.Image => new GenericTextRunProperties(_regular, _bodySize, foregroundBrush: _accent),
            RunStyle.Checkbox => new GenericTextRunProperties(_regular, _bodySize, foregroundBrush: _accent),
            RunStyle.Marker => new GenericTextRunProperties(_regular, _bodySize, foregroundBrush: _muted),
            _ => new GenericTextRunProperties(_regular, _bodySize, foregroundBrush: _fg),
        };
    }

    private double HeadingSize(int level) => level switch
    {
        1 => _bodySize * 1.9, 2 => _bodySize * 1.55, 3 => _bodySize * 1.3,
        4 => _bodySize * 1.15, 5 => _bodySize * 1.05, _ => _bodySize,
    };

    private Bitmap? LoadImage(string target)
    {
        if (_imageCache.TryGetValue(target, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            string? path = ImageResolver?.Invoke(target);
            if (path is not null && File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                bmp = Bitmap.DecodeToWidth(stream, 1600);
            }
        }
        catch { bmp = null; }
        _imageCache[target] = bmp;
        return bmp;
    }

    // ---- render ----

    public override void Render(DrawingContext context)
    {
        try { RenderContent(context); }
        catch (Exception ex)
        {
            context.FillRectangle(_bg, new Rect(Bounds.Size));
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "collectivewiki-render-crash.log"), $"{DateTimeOffset.Now:O}\n{ex}"); }
            catch { /* best effort */ }
        }
    }

    private void RenderContent(DrawingContext context)
    {
        context.FillRectangle(_bg, new Rect(Bounds.Size));
        if (Math.Abs(_measuredWidth - ContentWidth) > 0.5) { Measure(); _measuredWidth = ContentWidth; }
        double viewTop = _scrollY, viewBottom = _scrollY + Bounds.Height;
        int selStart = Math.Min(_anchor, _caret), selEnd = Math.Max(_anchor, _caret);
        bool hasSel = _selectionActive && selStart != selEnd;

        foreach (var v in _visuals)
        {
            if (v.Y + v.Height < viewTop || v.Y > viewBottom) continue;   // cull
            double sy = v.Y - _scrollY;
            var row = v.Row;

            switch (row.Kind)
            {
                case RowKind.Rule:
                    context.FillRectangle(_muted, new Rect(LeftPad, sy + 8, ContentWidth, 1));
                    break;
                case RowKind.Image when v.Image is { } bmp:
                    context.DrawImage(bmp, new Rect(LeftPad, sy + RowGap, v.ImageW, v.ImageH));
                    break;
                case RowKind.Properties:
                    context.DrawRectangle(_cardBg, null, new RoundedRect(new Rect(LeftPad, sy, ContentWidth, v.Height - RowGap), 6));
                    v.Header?.Draw(context, new Point(LeftPad + 12, sy + 7));
                    if (_propsExpanded) v.Layout?.Draw(context, new Point(LeftPad + 12, sy + PropsHeaderHeight));
                    break;
                case RowKind.Table when v.Cells is not null && v.ColX is not null && v.RowY is not null:
                    DrawTable(context, v, sy);
                    break;
                default:
                    // Callout block: a tinted band + coloured left bar behind the row (text stays at
                    // LeftPad so caret/hit-testing are unaffected). Contiguous rows form one continuous box.
                    if (row.Callout is { } co)
                    {
                        var (tint, bar) = CalloutBrushes(co.Family);
                        double bx = Math.Max(2, LeftPad - 10);
                        context.FillRectangle(tint, new Rect(bx, sy, LeftPad - bx + ContentWidth, v.Height));
                        context.FillRectangle(bar, new Rect(bx, sy, 3, v.Height));
                    }
                    if (hasSel && row.Revealed && v.Layout is { } layout)
                        DrawSelection(context, v, layout, selStart, selEnd, sy);
                    v.Layout?.Draw(context, new Point(LeftPad, sy));
                    break;
            }
        }

        DrawCaret(context);
        DrawCompletion(context);
    }

    private void DrawSelection(DrawingContext context, RowVisual v, TextLayout layout, int selStart, int selEnd, double sy)
    {
        int rowStart = v.Row.RawStart;
        int rowEnd = rowStart + RowDisplayLength(v.Row);
        int a = Math.Max(selStart, rowStart) - rowStart;
        int b = Math.Min(selEnd, rowEnd) - rowStart;
        if (b <= a) { if (!(selStart <= rowEnd && selEnd > rowEnd)) return; }
        a = Math.Clamp(a, 0, RowDisplayLength(v.Row));
        b = Math.Clamp(b, 0, RowDisplayLength(v.Row));
        if (b > a)
            foreach (var rect in layout.HitTestTextRange(a, b - a))
                context.FillRectangle(_selection, new Rect(LeftPad + rect.X, sy + rect.Y, rect.Width, rect.Height));
        // selection spilling past the line end (covers the newline): a thin trailing block
        if (selEnd > rowEnd)
        {
            var tail = layout.HitTestTextPosition(RowDisplayLength(v.Row));
            context.FillRectangle(_selection, new Rect(LeftPad + tail.X, sy + tail.Y, _bodySize * 0.4, Math.Max(_lineHeight, tail.Height)));
        }
    }

    // Maps a callout colour family to a (tint background, accent bar) brush pair.
    private static (IBrush Tint, IBrush Bar) CalloutBrushes(string family)
    {
        (byte r, byte g, byte b) = family switch
        {
            "blue"   => ((byte)0x25, (byte)0x63, (byte)0xEB),
            "green"  => ((byte)0x05, (byte)0x96, (byte)0x69),
            "amber"  => ((byte)0xD9, (byte)0x77, (byte)0x06),
            "red"    => ((byte)0xDC, (byte)0x26, (byte)0x26),
            "purple" => ((byte)0x7C, (byte)0x3A, (byte)0xED),
            _        => ((byte)0x6B, (byte)0x72, (byte)0x80),
        };
        return (new SolidColorBrush(Color.FromArgb(0x22, r, g, b)), new SolidColorBrush(Color.FromRgb(r, g, b)));
    }

    private void DrawTable(DrawingContext context, RowVisual v, double sy)
    {
        var colX = v.ColX!; var rowY = v.RowY!; var cells = v.Cells!;
        int gridRows = cells.GetLength(0); int cols = cells.GetLength(1);
        double right = colX[cols]; double bottom = rowY[gridRows];
        var pen = new Pen(_muted, 0.6);

        // Header band tint.
        context.FillRectangle(_cardBg, new Rect(LeftPad, sy, right, rowY[1]));

        // Grid lines.
        for (int r = 0; r <= gridRows; r++)
            context.DrawLine(pen, new Point(LeftPad, sy + rowY[r]), new Point(LeftPad + right, sy + rowY[r]));
        for (int c = 0; c <= cols; c++)
            context.DrawLine(pen, new Point(LeftPad + colX[c], sy), new Point(LeftPad + colX[c], sy + bottom));

        // Cell text.
        for (int r = 0; r < gridRows; r++)
            for (int c = 0; c < cols; c++)
                cells[r, c].Draw(context, new Point(LeftPad + colX[c] + TableLayout.CellPadX, sy + rowY[r] + 4));
    }

    private void DrawCaret(DrawingContext context)
    {
        if (_readMode || !IsFocused || !_caretOn) return;
        int caretLine = _doc.OffsetToPosition(_caret).Line;
        var v = _visuals.FirstOrDefault(x => x.Row.Kind == RowKind.Text && x.Row.Revealed && x.Row.FirstLine == caretLine);
        if (v?.Layout is null) return;
        int col = Math.Clamp(_caret - v.Row.RawStart, 0, RowDisplayLength(v.Row));
        var r = v.Layout.HitTestTextPosition(col);
        double sy = v.Y - _scrollY;
        context.FillRectangle(_caretBrush, new Rect(LeftPad + r.X, sy + r.Y, 1.6, Math.Max(_lineHeight, r.Height)));
    }

    private static int RowDisplayLength(EditorRow row)
        => row.DisplayToRaw.Length > 0 ? row.DisplayToRaw.Length - 1 : 0;

    // ---- [[ autocomplete ----

    // Recomputed on every rebuild (edits + caret moves): the popup shows whenever the caret sits in a
    // [[ context, unless the user Esc-dismissed exactly this context.
    private void UpdateCompletion()
    {
        if (_readMode) { _completion = null; _completionItems = Array.Empty<string>(); return; }
        string text = _doc.GetText();
        // Precedence: [[ wikilink → # tag → / slash-command; each fills in only when the prior has no context.
        var linkCtx = LinkCandidates is null ? null : LinkCompletion.Detect(text, _caret);
        var tagCtx = linkCtx is null && TagCandidates is not null ? TagCompletion.Detect(text, _caret) : null;
        var slashCtx = linkCtx is null && tagCtx is null ? SlashCompletion.Detect(text, _caret) : null;
        int? queryStart = linkCtx?.QueryStart ?? tagCtx?.QueryStart ?? (slashCtx is { } s ? s.SlashPos + 1 : null);
        if (queryStart is not { } qs || qs != _completionDismissedAt) _completionDismissedAt = -1;
        if (queryStart is null || _completionDismissedAt >= 0)
        {
            _completion = null;
            _completionItems = Array.Empty<string>();
            return;
        }
        if (linkCtx is { } lc)
        {
            _completionKind = CompletionKind.Link;
            _completion = lc;
            _completionItems = LinkCandidates!(lc.Query);
        }
        else if (tagCtx is { } tc)
        {
            _completionKind = CompletionKind.Tag;
            _tagCtx = tc;
            _completion = new LinkCompletion.Context(tc.QueryStart, tc.Query, -1);   // reuse the popup anchor
            _completionItems = TagCandidates!(tc.Query);
        }
        else
        {
            var sc = slashCtx!.Value;
            _completionKind = CompletionKind.Slash;
            _slashCtx = sc;
            _completion = new LinkCompletion.Context(sc.SlashPos + 1, sc.Query, -1);
            var cands = SlashCommands.Candidates(sc.Query);
            _slashIds = cands.Select(c => c.Id).ToList();
            _completionItems = cands.Select(c => c.Label).ToList();
        }
        _completionSel = Math.Clamp(_completionSel, 0, Math.Max(0, _completionItems.Count - 1));
    }

    private void CommitCompletion(int index)
    {
        if (_completion is not { } ctx || index < 0 || index >= _completionItems.Count) return;
        string text = _doc.GetText();
        if (_completionKind == CompletionKind.Slash)
        {
            string id = _slashIds[index];
            var (removed, sel) = SlashCompletion.RemoveTrigger(text, _slashCtx, _caret);
            ApplyFormat(new MarkdownEditing.FormatResult(removed, sel, sel));   // drop the "/query"
            ApplySlashCommand(id);                                              // then run the command
            return;
        }
        ApplyFormat(_completionKind == CompletionKind.Tag
            ? TagCompletion.Commit(text, _tagCtx, _caret, _completionItems[index])
            : LinkCompletion.Commit(text, ctx, _caret, _completionItems[index]));
    }

    // Applies a chosen slash command via the surface's existing edit ops. Only `template` needs the head.
    private void ApplySlashCommand(string id)
    {
        switch (id)
        {
            case "h1": SetHeading(1); break;
            case "h2": SetHeading(2); break;
            case "h3": SetHeading(3); break;
            case "bullet": ToggleBulletList(); break;
            case "numbered": ToggleNumberedList(); break;
            case "task": InsertAtCaret("- [ ] "); break;
            case "quote": ToggleQuote(); break;
            case "code": InsertCodeBlock(); break;
            case "callout": InsertAtCaret("> [!note] "); break;
            case "table": InsertTable(3, 3); break;
            case "date": InsertAtCaret(DateTime.Now.ToString("yyyy-MM-dd")); break;
            case "template": SlashCommandInvoked?.Invoke("template"); break;
        }
    }

    private void DismissCompletion()
    {
        if (_completion is { } ctx) _completionDismissedAt = ctx.QueryStart;
        _completion = null;
        _completionItems = Array.Empty<string>();
        InvalidateVisual();
    }

    // Draws the candidate popup anchored under the caret (flipped above when it would clip the bottom).
    private void DrawCompletion(DrawingContext context)
    {
        if (_completion is null || _completionItems.Count == 0) return;
        int caretLine = _doc.OffsetToPosition(_caret).Line;
        var v = _visuals.FirstOrDefault(x => x.Row.Kind == RowKind.Text && x.Row.Revealed && x.Row.FirstLine == caretLine);
        if (v?.Layout is null) { _completionBox = default; return; }
        int col = Math.Clamp(_caret - v.Row.RawStart, 0, RowDisplayLength(v.Row));
        var hit = v.Layout.HitTestTextPosition(col);

        _completionRowH = _lineHeight + 8;
        double w = 280, h = _completionItems.Count * _completionRowH + 8;
        double x = LeftPad + hit.X;
        double y = v.Y - _scrollY + hit.Y + Math.Max(_lineHeight, hit.Height) + 2;
        if (y + h > Bounds.Height) y = Math.Max(0, v.Y - _scrollY - h - 2);
        if (x + w > Bounds.Width) x = Math.Max(0, Bounds.Width - w - 4);

        _completionBox = new Rect(x, y, w, h);
        context.DrawRectangle(_bg, new Pen(_muted, 1), new RoundedRect(_completionBox, 6));
        for (int i = 0; i < _completionItems.Count; i++)
        {
            var rowRect = new Rect(x + 4, y + 4 + i * _completionRowH, w - 8, _completionRowH);
            if (i == _completionSel)
                context.DrawRectangle(_selection, null, new RoundedRect(rowRect, 4));
            var tl = new TextLayout(_completionItems[i], _regular, _bodySize, _fg,
                maxWidth: w - 24, maxLines: 1, textTrimming: TextTrimming.CharacterEllipsis);
            tl.Draw(context, new Point(rowRect.X + 8, rowRect.Y + 4));
        }
    }

    // ---- scrolling ----

    private double MaxScroll => Math.Max(0, _contentHeight - Bounds.Height);
    private void ClampScroll() => _scrollY = Math.Clamp(_scrollY, 0, MaxScroll);

    private void EnsureCaretVisible()
    {
        int caretLine = _doc.OffsetToPosition(_caret).Line;
        var v = _visuals.FirstOrDefault(x => x.Row.Kind == RowKind.Text && x.Row.FirstLine == caretLine);
        if (v is null) return;
        if (v.Y < _scrollY) _scrollY = v.Y - TopPad;
        else if (v.Y + v.Height > _scrollY + Bounds.Height) _scrollY = v.Y + v.Height - Bounds.Height + TopPad;
        ClampScroll();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Ctrl+wheel zooms the document (like a browser); a plain wheel scrolls.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Delta.Y != 0)
        {
            ChangeZoom(e.Delta.Y > 0 ? 1 : -1);
            e.Handled = true;
            base.OnPointerWheelChanged(e);
            return;
        }
        _scrollY = Math.Clamp(_scrollY - e.Delta.Y * _lineHeight * 3, 0, MaxScroll);
        InvalidateVisual();
        e.Handled = true;
        base.OnPointerWheelChanged(e);
    }

    // ---- hit testing ----

    // Returns the caret offset for a point, or -1 if the point hit a non-caret target (image/link) that
    // was already dispatched. `linkTarget`/`imageTarget` are set when a link/image was clicked.
    // The row whose vertical band contains the point (falls back to the last row).
    private RowVisual? RowVisualAt(Point p)
    {
        double y = p.Y + _scrollY;
        foreach (var v in _visuals)
            if (y >= v.Y && y < v.Y + v.Height + RowGap) return v;
        return _visuals.LastOrDefault();
    }

    private int OffsetAt(Point p, out string? linkTarget, out string? imageTarget, out int checkboxOffset)
    {
        linkTarget = null; imageTarget = null; checkboxOffset = -1;
        double y = p.Y + _scrollY;
        var hit = RowVisualAt(p);
        if (hit is null) return Math.Clamp(_caret, 0, _doc.Length);

        var row = hit.Row;
        switch (row.Kind)
        {
            case RowKind.Image:
                imageTarget = row.ImageTarget;
                return -1;
            case RowKind.Rule:
            case RowKind.Properties:
            case RowKind.Table:
                return row.RawStart;   // caret to the block start → the block reveals raw for editing
            default:
                if (hit.Layout is null) return row.RawStart;
                double localX = p.X - LeftPad;
                double localY = y - hit.Y;
                var res = hit.Layout.HitTestPoint(new Point(localX, localY));
                int dispPos = Math.Clamp(res.TextPosition + (res.IsTrailing ? 1 : 0), 0, RowDisplayLength(row));
                // a click on a wikilink/image run activates it rather than moving the caret
                var run = RunAtDisplay(row, dispPos == RowDisplayLength(row) ? Math.Max(0, dispPos - 1) : dispPos);
                if (run is { Style: RunStyle.WikiLink or RunStyle.Link } && run.LinkTarget is { } lt) { linkTarget = lt; return -1; }
                if (run is { Style: RunStyle.Image } && run.LinkTarget is { } it) { imageTarget = it; return -1; }
                if (run is { Style: RunStyle.Checkbox } && run.LinkTarget is { } cb && int.TryParse(cb, out var co)) { checkboxOffset = co; return -1; }
                int idx = Math.Clamp(dispPos, 0, row.DisplayToRaw.Length - 1);
                return row.DisplayToRaw[idx];
        }
    }

    private static StyledRun? RunAtDisplay(EditorRow row, int dispPos)
    {
        int pos = 0;
        foreach (var run in row.Runs)
        {
            if (dispPos >= pos && dispPos < pos + run.Text.Length) return run;
            pos += run.Text.Length;
        }
        return row.Runs.Count > 0 ? row.Runs[^1] : null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        bool left = props.IsLeftButtonPressed;
        bool middle = props.IsMiddleButtonPressed;
        if (!left && !middle) { base.OnPointerPressed(e); return; }

        var pos = e.GetPosition(this);

        // A click inside the [[ autocomplete popup commits that row.
        if (left && _completion is not null && _completionItems.Count > 0 && _completionBox.Contains(pos))
        {
            int idx = (int)((pos.Y - _completionBox.Y - 4) / _completionRowH);
            CommitCompletion(idx);
            e.Handled = true; base.OnPointerPressed(e); return;
        }

        // Front-matter Properties card: single-click toggles collapse, double-click opens the typed editor.
        if (left && RowVisualAt(pos)?.Row.Kind == RowKind.Properties)
        {
            if (e.ClickCount >= 2) PropertiesEditRequested?.Invoke(this, EventArgs.Empty);
            else { _propsExpanded = !_propsExpanded; Measure(); InvalidateVisual(); }
            e.Handled = true; base.OnPointerPressed(e); return;
        }

        if (left) Focus();
        int off = OffsetAt(pos, out var link, out var image, out var checkbox);

        // A wikilink: plain left-click navigates the current tab; Ctrl- or middle-click opens a new one.
        if (link is not null)
        {
            bool newTab = middle || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            WikiLinkActivated?.Invoke(link, newTab);
            e.Handled = true; base.OnPointerPressed(e); return;
        }
        if (image is not null) { ImageActivated?.Invoke(image); e.Handled = true; base.OnPointerPressed(e); return; }
        if (checkbox >= 0)
        {
            ToggleCheckbox(checkbox);
            e.Handled = true; base.OnPointerPressed(e); return;
        }

        // Reading view: links/images/checkboxes above stay live, but there is no caret to place.
        if (_readMode) { e.Handled = true; base.OnPointerPressed(e); return; }

        // Middle-click off a link does nothing in the body (no caret move).
        if (!left) { base.OnPointerPressed(e); return; }

        // Double-click selects the word; triple-click selects the line.
        if (off >= 0 && e.ClickCount == 2)
        {
            var (ws, we) = WordNav.WordAt(_doc.GetText(), off);
            _anchor = ws; _caret = we; _selectionActive = we > ws;
            Rebuild(); InvalidateVisual();
            e.Handled = true; base.OnPointerPressed(e); return;
        }
        if (off >= 0 && e.ClickCount >= 3)
        {
            _anchor = LineStart(off); _caret = LineEnd(off); _selectionActive = _caret > _anchor;
            Rebuild(); InvalidateVisual();
            e.Handled = true; base.OnPointerPressed(e); return;
        }

        if (off >= 0)
        {
            bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            _caret = off; if (!extend) _anchor = off;
            _selectionActive = extend;
            _dragging = true;
            WakeCaret();
            e.Pointer.Capture(this);
            Rebuild(); EnsureCaretVisible(); InvalidateVisual();
        }
        e.Handled = true;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragging)
        {
            int off = OffsetAt(e.GetPosition(this), out _, out _, out _);
            if (off >= 0)
            {
                _caret = off; _selectionActive = _caret != _anchor;
                Rebuild(); EnsureCaretVisible(); InvalidateVisual();
            }
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragging) { _dragging = false; e.Pointer.Capture(null); }
        base.OnPointerReleased(e);
    }

    // ---- editing ----

    private bool HasSelection => _selectionActive && _anchor != _caret;
    private int SelStart => Math.Min(_anchor, _caret);
    private int SelEnd => Math.Max(_anchor, _caret);

    // Set the caret/selection BEFORE mutating the document: _doc edits fire Changed → Rebuild
    // synchronously, and the rebuild computes the reveal plan from the caret. Updating the caret after
    // the edit left the plan one keystroke stale — Enter revealed the OLD line, so the caret sat on an
    // unrevealed row and DrawCaret skipped it (the field-reported vanishing cursor). ApplyFormat always
    // had this order right; every primitive edit goes through here now.
    private void ApplyEdit(int start, int remove, string insert, int caretAfter)
    {
        if (_readMode) return;   // reading view never edits
        _caret = caretAfter; _anchor = caretAfter; _selectionActive = false;
        WakeCaret();
        if (remove > 0 && insert.Length > 0) _doc.Replace(start, remove, insert);
        else if (remove > 0) _doc.Delete(start, remove);
        else if (insert.Length > 0) _doc.Insert(start, insert);
    }

    private void DeleteSelection()
    {
        if (!HasSelection) return;
        int s = SelStart;
        ApplyEdit(s, SelEnd - s, "", s);
    }

    private void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        DeleteSelection();
        ApplyEdit(_caret, 0, text, _caret + text.Length);
    }

    // Flip a task checkbox's state character ([ ] <-> [x]) as one edit; the doc change rebuilds + marks dirty.
    private void ToggleCheckbox(int stateOffset)
    {
        if (stateOffset < 0 || stateOffset >= _doc.Length) return;
        string cur = _doc.GetText(stateOffset, 1);
        string next = cur == " " ? "x" : " ";
        _doc.Replace(stateOffset, 1, next);   // fires OnDocChanged -> Rebuild + TextChanged
    }

    // ---- formatting commands (toolbar + shortcuts) ----
    // Each computes a pure MarkdownEditing transform over the whole document then applies it as one
    // TextDocument.Replace (a single undo unit). The caret/selection is set from the transform's result.

    public void ToggleBold() => ApplyFormat(MarkdownEditing.Wrap(_doc.GetText(), SelStart, SelEnd, "**", "**"));
    public void ToggleItalic() => ApplyFormat(MarkdownEditing.Wrap(_doc.GetText(), SelStart, SelEnd, "*", "*"));
    public void ToggleStrikethrough() => ApplyFormat(MarkdownEditing.Wrap(_doc.GetText(), SelStart, SelEnd, "~~", "~~"));
    public void ToggleHighlight() => ApplyFormat(MarkdownEditing.Wrap(_doc.GetText(), SelStart, SelEnd, "==", "=="));
    public void ToggleInlineCode() => ApplyFormat(MarkdownEditing.Wrap(_doc.GetText(), SelStart, SelEnd, "`", "`"));
    public void ToggleBulletList() => ApplyFormat(MarkdownEditing.ToggleLinePrefix(_doc.GetText(), SelStart, SelEnd, "- "));
    public void ToggleNumberedList() => ApplyFormat(MarkdownEditing.ToggleOrderedList(_doc.GetText(), SelStart, SelEnd));
    public void ToggleQuote() => ApplyFormat(MarkdownEditing.ToggleLinePrefix(_doc.GetText(), SelStart, SelEnd, "> "));
    public void SetHeading(int level) => ApplyFormat(MarkdownEditing.SetHeading(_doc.GetText(), SelStart, SelEnd, level));
    public void InsertCodeBlock() => ApplyFormat(MarkdownEditing.FenceBlock(_doc.GetText(), SelStart, SelEnd));
    public void InsertWikiLink() => ApplyFormat(MarkdownEditing.Wrap(_doc.GetText(), SelStart, SelEnd, "[[", "]]"));

    /// <summary>Indent (+1) / outdent (-1) the caret's list line(s) — the toolbar's indent buttons. A no-op
    /// where <see cref="MarkdownEditing"/> returns null (e.g. a single non-list line, or nothing to outdent).</summary>
    public void ChangeIndent(int delta)
    {
        var r = delta >= 0
            ? MarkdownEditing.IndentLines(_doc.GetText(), SelStart, SelEnd)
            : MarkdownEditing.OutdentLines(_doc.GetText(), SelStart, SelEnd);
        if (r is { } res) ApplyFormat(res);
    }

    /// <summary>Insert a GFM pipe table (its own block) at the caret — the toolbar's Insert Table.</summary>
    public void InsertTable(int rows, int cols)
        => ApplyFormat(MarkdownEditing.InsertTableBlock(_doc.GetText(), SelStart, SelEnd, rows, cols));

    private void ApplyFormat(MarkdownEditing.FormatResult r)
    {
        if (_readMode) return;   // reading view never edits
        _anchor = Math.Clamp(r.SelStart, 0, r.Text.Length);
        _caret = Math.Clamp(r.SelEnd, 0, r.Text.Length);
        _selectionActive = _anchor != _caret;
        WakeCaret();
        if (r.Text != _doc.GetText())
            _doc.Replace(0, _doc.Length, r.Text);   // fires OnDocChanged → Rebuild + TextChanged (marks dirty)
        else
            Rebuild();                               // selection-only change (a no-op toggle)
        EnsureCaretVisible();
        InvalidateVisual();
    }

    /// <summary>Replaces the whole document with <paramref name="newText"/> as one undo unit (used by the
    /// typed Properties editor to write the rebuilt front-matter back). Clamps the caret into range.</summary>
    public void ReplaceDocument(string newText)
    {
        int c = Math.Clamp(_caret, 0, newText.Length);
        ApplyFormat(new MarkdownEditing.FormatResult(newText, c, c));
    }

    // ---- find (Ctrl+F) ----

    /// <summary>The whole document text — the find bar searches this.</summary>
    public string CurrentText => _doc.GetText();

    /// <summary>The current caret offset — the find bar starts its next-match walk here.</summary>
    public int CaretOffset => _caret;

    /// <summary>The current selection span <c>[Start, End)</c> as absolute offsets (Start==End when collapsed)
    /// — a formatting toolbar reads this + <see cref="CurrentText"/> to detect the caret's formatting state.</summary>
    public (int Start, int End) SelectionSpan => (SelStart, SelEnd);

    /// <summary>Raised when the user asks to find (Ctrl+F / the Find in Note menu); the host shows a find bar
    /// seeded with the current selection. The bar then drives <see cref="CurrentText"/>/<see cref="CaretOffset"/>
    /// + <see cref="SelectRange"/> — the custom-drawn surface hosts no child controls of its own.</summary>
    public event EventHandler<string>? FindRequested;

    public void OpenFind()
        => FindRequested?.Invoke(this, HasSelection ? _doc.GetText(SelStart, SelEnd - SelStart) : "");

    /// <summary>Raised on Ctrl+H / the Replace menu item; the host shows the find bar with its replace row.</summary>
    public event EventHandler<string>? ReplaceRequested;

    public void OpenReplace()
        => ReplaceRequested?.Invoke(this, HasSelection ? _doc.GetText(SelStart, SelEnd - SelStart) : "");

    /// <summary>Replaces [start, start+length) with <paramref name="replacement"/> as one edit, leaving the
    /// caret after it — the find bar's Replace/Replace-All apply through this (the doc change rebuilds +
    /// marks the tab dirty).</summary>
    public void ReplaceRange(int start, int length, string replacement)
    {
        start = Math.Clamp(start, 0, _doc.Length);
        length = Math.Clamp(length, 0, _doc.Length - start);
        ApplyEdit(start, length, replacement ?? "", start + (replacement?.Length ?? 0));
        EnsureCaretVisible(); InvalidateVisual();
    }

    /// <summary>Inserts <paramref name="text"/> at the caret (replacing any selection) as one edit — used by
    /// "Insert Template". The document change triggers a rebuild + marks the tab dirty.</summary>
    public void InsertAtCaret(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        InsertText(text);
        EnsureCaretVisible();
        InvalidateVisual();
    }

    /// <summary>Selects [start, start+length) and scrolls it into view — the find bar highlights a match this
    /// way (the match line becomes the revealed caret line with the match selected).</summary>
    public void SelectRange(int start, int length)
    {
        _anchor = Math.Clamp(start, 0, _doc.Length);
        _caret = Math.Clamp(start + length, 0, _doc.Length);
        _selectionActive = _anchor != _caret;
        Rebuild();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    /// <summary>Selects the first case-insensitive occurrence of <paramref name="query"/> and scrolls it into
    /// view — used to land a vault-search / tag click on the matched text instead of the top of the note.
    /// No-op when the query is empty or not present.</summary>
    public void SelectFirstMatch(string query)
    {
        if (string.IsNullOrEmpty(query)) return;
        var matches = InNoteFinder.Find(_doc.GetText(), query, matchCase: false);
        if (matches.Count > 0) SelectRange(matches[0].Start, matches[0].Length);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (!_readMode && !string.IsNullOrEmpty(e.Text))
        {
            InsertText(e.Text);
            EnsureCaretVisible();
            e.Handled = true;
        }
        base.OnTextInput(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // Zoom (Ctrl +/-/0) works in both edit and reading view, so handle it before either branch.
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.OemPlus or Key.Add: ChangeZoom(1); e.Handled = true; return;
                case Key.OemMinus or Key.Subtract: ChangeZoom(-1); e.Handled = true; return;
                case Key.D0 or Key.NumPad0: ResetZoom(); e.Handled = true; return;
            }
        }

        // Reading view: only view navigation — everything else bubbles to the window (menus, palette).
        if (_readMode)
        {
            switch (e.Key)
            {
                case Key.Up: _scrollY = Math.Clamp(_scrollY - _lineHeight * 3, 0, MaxScroll); InvalidateVisual(); e.Handled = true; return;
                case Key.Down: _scrollY = Math.Clamp(_scrollY + _lineHeight * 3, 0, MaxScroll); InvalidateVisual(); e.Handled = true; return;
                case Key.PageUp: _scrollY = Math.Clamp(_scrollY - Bounds.Height * 0.9, 0, MaxScroll); InvalidateVisual(); e.Handled = true; return;
                case Key.PageDown: _scrollY = Math.Clamp(_scrollY + Bounds.Height * 0.9, 0, MaxScroll); InvalidateVisual(); e.Handled = true; return;
                case Key.Home when ctrl: _scrollY = 0; InvalidateVisual(); e.Handled = true; return;
                case Key.End when ctrl: _scrollY = MaxScroll; InvalidateVisual(); e.Handled = true; return;
            }
            base.OnKeyDown(e);
            return;
        }

        // The [[ autocomplete popup owns Up/Down/Enter/Tab/Esc while it shows.
        if (_completion is not null && _completionItems.Count > 0)
        {
            switch (e.Key)
            {
                case Key.Down:
                    _completionSel = (_completionSel + 1) % _completionItems.Count;
                    InvalidateVisual(); e.Handled = true; return;
                case Key.Up:
                    _completionSel = (_completionSel + _completionItems.Count - 1) % _completionItems.Count;
                    InvalidateVisual(); e.Handled = true; return;
                case Key.Enter or Key.Tab:
                    CommitCompletion(_completionSel); e.Handled = true; return;
                case Key.Escape:
                    DismissCompletion(); e.Handled = true; return;
            }
        }

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.C: Copy(); e.Handled = true; return;
                case Key.X: Cut(); e.Handled = true; return;
                case Key.V: Paste(); e.Handled = true; return;
                case Key.A: SelectAll(); e.Handled = true; return;
                case Key.Z: _doc.Undo(); ClampCaret(); e.Handled = true; return;
                case Key.Y: _doc.Redo(); ClampCaret(); e.Handled = true; return;
                case Key.S: SaveRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return;
                case Key.B: ToggleBold(); e.Handled = true; return;
                case Key.I: ToggleItalic(); e.Handled = true; return;
                case Key.K: InsertWikiLink(); e.Handled = true; return;
                case Key.F: OpenFind(); e.Handled = true; return;
                case Key.H: OpenReplace(); e.Handled = true; return;
                case Key.Home: SetCaret(0, shift); e.Handled = true; return;
                case Key.End: SetCaret(_doc.Length, shift); e.Handled = true; return;
                case Key.Left: SetCaret(WordNav.PrevBoundary(_doc.GetText(), _caret), shift); e.Handled = true; return;
                case Key.Right: SetCaret(WordNav.NextBoundary(_doc.GetText(), _caret), shift); e.Handled = true; return;
                case Key.Back:
                    if (HasSelection) DeleteSelection();
                    else if (_caret > 0) { int ws = WordNav.PrevBoundary(_doc.GetText(), _caret); ApplyEdit(ws, _caret - ws, "", ws); }
                    EnsureCaretVisible(); e.Handled = true; return;
                case Key.Delete:
                    if (HasSelection) DeleteSelection();
                    else if (_caret < _doc.Length) { int we = WordNav.NextBoundary(_doc.GetText(), _caret); ApplyEdit(_caret, we - _caret, "", _caret); }
                    EnsureCaretVisible(); e.Handled = true; return;
            }
        }

        // Alt+arrows are the window's Back/Forward — let them bubble instead of moving the caret.
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        switch (e.Key)
        {
            case Key.Left when !alt: SetCaret(_caret - 1, shift); e.Handled = true; return;
            case Key.Right when !alt: SetCaret(_caret + 1, shift); e.Handled = true; return;
            case Key.Up: MoveVertical(-1, shift); e.Handled = true; return;
            case Key.Down: MoveVertical(1, shift); e.Handled = true; return;
            case Key.Home: SetCaret(LineStart(_caret), shift); e.Handled = true; return;
            case Key.End: SetCaret(LineEnd(_caret), shift); e.Handled = true; return;
            case Key.Back: Backspace(); e.Handled = true; return;
            case Key.Delete: DeleteForward(); e.Handled = true; return;
            case Key.Enter:
                if (!HasSelection && MarkdownEditing.ContinueLine(_doc.GetText(), _caret) is { } cont)
                    ApplyFormat(cont);
                else { InsertText("\n"); EnsureCaretVisible(); }
                e.Handled = true; return;
            case Key.Tab when shift:
                if (MarkdownEditing.OutdentLines(_doc.GetText(), SelStart, SelEnd) is { } od) ApplyFormat(od);
                e.Handled = true; return;
            case Key.Tab:
                if (MarkdownEditing.IndentLines(_doc.GetText(), SelStart, SelEnd) is { } id) ApplyFormat(id);
                else { InsertText("    "); EnsureCaretVisible(); }
                e.Handled = true; return;
            case Key.PageUp: _scrollY = Math.Clamp(_scrollY - Bounds.Height * 0.9, 0, MaxScroll); InvalidateVisual(); e.Handled = true; return;
            case Key.PageDown: _scrollY = Math.Clamp(_scrollY + Bounds.Height * 0.9, 0, MaxScroll); InvalidateVisual(); e.Handled = true; return;
        }
        base.OnKeyDown(e);
    }

    private void Backspace()
    {
        if (HasSelection) DeleteSelection();
        else if (_caret > 0) ApplyEdit(_caret - 1, 1, "", _caret - 1);
        EnsureCaretVisible();
    }

    private void DeleteForward()
    {
        if (HasSelection) DeleteSelection();
        else if (_caret < _doc.Length) ApplyEdit(_caret, 1, "", _caret);
        EnsureCaretVisible();
    }

    private void SetCaret(int offset, bool extend)
    {
        _caret = Math.Clamp(offset, 0, _doc.Length);
        if (!extend) _anchor = _caret;
        _selectionActive = extend && _anchor != _caret;
        WakeCaret();
        Rebuild(); EnsureCaretVisible(); InvalidateVisual();
    }

    private void ClampCaret()
    {
        _caret = Math.Clamp(_caret, 0, _doc.Length); _anchor = _caret; _selectionActive = false;
        Rebuild(); EnsureCaretVisible(); InvalidateVisual();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private int LineStart(int offset) => _doc.GetLineStartOffset(_doc.OffsetToPosition(offset).Line);
    private int LineEnd(int offset)
    {
        var pos = _doc.OffsetToPosition(offset);
        return _doc.GetLineStartOffset(pos.Line) + _doc.GetLineLength(pos.Line);
    }

    private void MoveVertical(int delta, bool extend)
    {
        var pos = _doc.OffsetToPosition(_caret);
        int line = Math.Clamp(pos.Line + delta, 0, _doc.LineCount - 1);
        int col = Math.Min(pos.Column, _doc.GetLineLength(line));
        SetCaret(_doc.PositionToOffset(new TextPosition(line, col)), extend);
    }

    private void SelectAll() { _anchor = 0; _caret = _doc.Length; _selectionActive = _doc.Length > 0; Rebuild(); InvalidateVisual(); }

    private void Copy()
    {
        if (!HasSelection) return;
        string text = _doc.GetText(SelStart, SelEnd - SelStart);
        if (!string.IsNullOrEmpty(text)) TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
    }

    private void Cut() { if (!HasSelection) return; Copy(); DeleteSelection(); EnsureCaretVisible(); InvalidateVisual(); }

    private async void Paste()
    {
        var cb = TopLevel.GetTopLevel(this)?.Clipboard;
        if (cb is null) return;

        // An image on the clipboard becomes a saved asset + embed; otherwise fall back to text.
        if (ImageSaver is not null && await TryPasteImageAsync(cb)) return;

        var data = await cb.TryGetDataAsync();
        string? text = data is null ? null : await data.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text)) { InsertText(text.Replace("\r\n", "\n").Replace('\r', '\n')); EnsureCaretVisible(); InvalidateVisual(); }
    }

    private async Task<bool> TryPasteImageAsync(IClipboard cb)
    {
        // 1) A bitmap on the clipboard (a screenshot, or "Copy image" from a browser). Avalonia decodes
        //    it for us; re-encode to PNG bytes for the vault.
        try
        {
            var bmp = await cb.TryGetBitmapAsync();
            if (bmp is not null)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms);
                return InsertImageEmbed(ms.ToArray(), "png");
            }
        }
        catch { /* no bitmap / decode failure — fall through */ }

        // 2) A copied image file (Explorer/Finder): read its bytes, keep its extension.
        try
        {
            var files = await cb.TryGetFilesAsync();
            if (files is not null)
                foreach (var item in files)
                {
                    string? path = item.TryGetLocalPath();
                    if (path is null || !IsImageFile(path)) continue;
                    return InsertImageEmbed(File.ReadAllBytes(path), Path.GetExtension(path).TrimStart('.'));
                }
        }
        catch { /* unreadable file — fall through to text */ }

        return false;
    }

    private bool InsertImageEmbed(byte[] bytes, string extension)
    {
        string? target = ImageSaver?.Invoke(bytes, extension);
        if (string.IsNullOrEmpty(target)) return false;
        // Place the embed on its own line with the caret dropped below it (not inline at the caret): the
        // surface only draws an image widget for a line that is nothing but the embed AND isn't the caret's
        // revealed line, so an inline insert would just show raw '![[…]]' text. See MarkdownEditing.InsertImageEmbed.
        ApplyFormat(MarkdownEditing.InsertImageEmbed(_doc.GetText(), SelStart, SelEnd, target));
        return true;
    }

    private static bool IsImageFile(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";

    // ---- drag & drop (image files → vault assets + embeds) ----

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool accept = !_readMode && ImageSaver is not null && e.DataTransfer is { } dt && dt.Contains(DataFormat.File);
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_readMode || ImageSaver is null || e.DataTransfer is not { } dt) return;
        var files = dt.TryGetFiles();
        if (files is null) return;

        // Drop at the pointer: move the caret there first so the embed lands where the user aimed
        // (end of the note when the drop is on empty space below the content).
        int off = OffsetAt(e.GetPosition(this), out _, out _, out _);
        SetCaret(off >= 0 ? off : _doc.Length, extend: false);

        bool any = false;
        foreach (var item in files)
        {
            string? path = item.TryGetLocalPath();
            if (path is null || !IsImageFile(path)) continue;
            try
            {
                if (InsertImageEmbed(File.ReadAllBytes(path), Path.GetExtension(path).TrimStart('.')))
                    any = true;
            }
            catch (IOException) { /* unreadable file — skip it, keep the rest of the drop */ }
        }
        if (any) { Focus(); EnsureCaretVisible(); InvalidateVisual(); }
        e.Handled = true;
    }

    // ---- lifecycle ----

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => Focus());
        _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blink.Tick += (_, _) => { _caretOn = !_caretOn; if (IsFocused) InvalidateVisual(); };
        _blink.Start();
    }

    // Typing or moving must show the caret immediately: force it on and restart the blink phase, so an
    // edit never lands in the invisible half of the blink cycle (Enter onto an empty line had no glyph
    // feedback either, which read as the cursor vanishing).
    private void WakeCaret()
    {
        _caretOn = true;
        if (_blink is { } b) { b.Stop(); b.Start(); }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _blink?.Stop(); _blink = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;

    private sealed class RowVisual
    {
        public EditorRow Row = null!;
        public TextLayout? Layout;
        public TextLayout? Header;   // the Properties card's disclosure header (Properties rows only)
        public Bitmap? Image;
        public double Y;
        public double Height;
        public double ImageW;
        public double ImageH;
        public double[]? ColX;          // left x of each column (relative to LeftPad), + a trailing right edge
        public double[]? RowY;          // top y of each grid row (header + body), + a trailing bottom edge
        public TextLayout[,]? Cells;    // [gridRow, col] cell text ([0,*] = header)
    }
}
