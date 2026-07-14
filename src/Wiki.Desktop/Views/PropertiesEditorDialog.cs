// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Collective.Platform.Controls;
using Wiki.Editor;

namespace Wiki.Desktop.Views;

/// <summary>Typed editor for a note's YAML front-matter Properties (opened by double-clicking the
/// Properties card or View ▸ Edit Properties). Each property is a key + a type (Text / List / Number /
/// Date / Checkbox) + a type-appropriate value editor; Save rebuilds the front-matter via the pure
/// <see cref="FrontmatterModel"/> and returns the new note text. Code-built to match the head's other
/// dialogs. On-screen interaction is PENDING real-desktop confirmation (headless host).</summary>
public sealed class PropertiesEditorDialog : DialogWindow
{
    private PropertiesEditorDialog() { }

    /// <summary>Shows the editor modally. Returns the rebuilt note text if the user saved, else null.</summary>
    public static async Task<string?> ShowAsync(Window owner, string noteText)
    {
        var doc = FrontmatterModel.Parse(noteText);
        var rows = new List<PropertyRow>();
        var rowPanel = new StackPanel { Spacing = 6 };

        void AddRow(FrontmatterProperty p)
        {
            var row = new PropertyRow(p);
            row.DeleteRequested += r => { rows.Remove(r); rowPanel.Children.Remove(r.Panel); };
            rows.Add(row);
            rowPanel.Children.Add(row.Panel);
        }

        foreach (var p in doc.Properties) AddRow(p);

        var add = new Button { Content = "＋ Add property" };
        add.Click += (_, _) => AddRow(FrontmatterProperty.Scalar("", PropertyType.Text, ""));

        string? result = null;
        var ok = new Button { Content = "Save", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var dlg = new PropertiesEditorDialog
        {
            Title = "Edit Properties", Width = 500, Height = 460, SizeToContent = SizeToContent.Manual,
            Content = new DockPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0),
                        Children = { ok, cancel },
                    },
                    new Border { [DockPanel.DockProperty] = Dock.Bottom, Child = add, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left },
                    new ScrollViewer { Content = rowPanel },
                },
            },
        };

        ok.Click += (_, _) =>
        {
            var props = rows.Select(r => r.Build()).Where(p => p is not null).Select(p => p!).ToList();
            result = FrontmatterModel.ApplyTo(noteText, props);
            dlg.Close();
        };
        cancel.Click += (_, _) => dlg.Close();

        await dlg.ShowDialog(owner);
        return result;
    }

    // One editable property: key box + type dropdown + a value editor swapped to match the type.
    private sealed class PropertyRow
    {
        public Grid Panel { get; }
        public event Action<PropertyRow>? DeleteRequested;

        private readonly TextBox _key;
        private readonly ComboBox _type;
        private readonly Border _valueHost = new();
        private Control _valueEditor;
        private PropertyType _currentType;

        public PropertyRow(FrontmatterProperty p)
        {
            _currentType = p.Type;
            _key = new TextBox { Text = p.Key, PlaceholderText = "key", MinWidth = 120 };
            AutomationProperties.SetName(_key, "Property name");
            _type = new ComboBox { ItemsSource = Enum.GetValues<PropertyType>(), SelectedItem = p.Type, MinWidth = 96 };
            _valueEditor = BuildEditor(p.Type, p);
            _valueHost.Child = _valueEditor;

            _type.SelectionChanged += (_, _) =>
            {
                if (_type.SelectedItem is PropertyType t && t != _currentType)
                {
                    var carry = Snapshot();
                    _currentType = t;
                    _valueEditor = BuildEditor(t, carry);
                    _valueHost.Child = _valueEditor;
                }
            };

            var del = new Button { Content = "✕", Width = 30 };
            AutomationProperties.SetName(del, "Delete property");
            del.Click += (_, _) => DeleteRequested?.Invoke(this);

            Panel = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 6 };
            _valueHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(_key, 0);
            Grid.SetColumn(_type, 1);
            Grid.SetColumn(_valueHost, 2);
            Grid.SetColumn(del, 3);
            Panel.Children.Add(_key);
            Panel.Children.Add(_type);
            Panel.Children.Add(_valueHost);
            Panel.Children.Add(del);
        }

        // Reads the current editor's value into a property snapshot (used to carry data across a type change).
        private FrontmatterProperty Snapshot()
        {
            string key = _key.Text ?? "";
            return _currentType switch
            {
                PropertyType.List => FrontmatterProperty.List(key, SplitList((_valueEditor as TextBox)?.Text)),
                PropertyType.Checkbox => FrontmatterProperty.Scalar(key, PropertyType.Checkbox, (_valueEditor as CheckBox)?.IsChecked == true ? "true" : "false"),
                PropertyType.Date => FrontmatterProperty.Scalar(key, PropertyType.Date, DateString((_valueEditor as DatePicker)?.SelectedDate)),
                _ => FrontmatterProperty.Scalar(key, _currentType, (_valueEditor as TextBox)?.Text ?? ""),
            };
        }

        // Builds the value item, returns null for a blank key (dropped on save).
        public FrontmatterProperty? Build()
        {
            var snap = Snapshot();
            return string.IsNullOrWhiteSpace(snap.Key) ? null : snap with { Key = snap.Key.Trim() };
        }

        private static Control BuildEditor(PropertyType t, FrontmatterProperty seed) => t switch
        {
            PropertyType.Checkbox => new CheckBox { IsChecked = seed.Value.Equals("true", StringComparison.OrdinalIgnoreCase) },
            PropertyType.Date => new DatePicker { SelectedDate = ParseDate(seed.Value) },
            PropertyType.List => new TextBox { Text = string.Join(", ", seed.Items), PlaceholderText = "a, b, c" },
            _ => new TextBox { Text = seed.Value, PlaceholderText = "value" },
        };

        private static IReadOnlyList<string> SplitList(string? text)
            => (text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        private static DateTimeOffset? ParseDate(string v)
            => DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        private static string DateString(DateTimeOffset? d)
            => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    }
}
