// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Desktop;

/// <summary>Per-window, browser-style note-navigation history. <see cref="Visit"/> records the active
/// note as the user navigates (consecutive duplicates collapse; a visit after Back drops the forward
/// tail); <see cref="GoBack"/>/<see cref="GoForward"/> move the cursor without recording. Pure and
/// unit-tested; the view-model owns the re-entrancy guard so history jumps don't re-record.</summary>
public sealed class NavigationHistory
{
    private const int Cap = 200;
    private readonly List<string> _entries = new();
    private int _index = -1;

    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _entries.Count - 1;

    public void Visit(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (_index >= 0 && _entries[_index] == path) return;
        if (_index < _entries.Count - 1) _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(path);
        if (_entries.Count > Cap) _entries.RemoveAt(0);
        _index = _entries.Count - 1;
    }

    public string? GoBack() => CanGoBack ? _entries[--_index] : null;
    public string? GoForward() => CanGoForward ? _entries[++_index] : null;

    public void Clear() { _entries.Clear(); _index = -1; }
}
