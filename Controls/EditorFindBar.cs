using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace SchemaCompare.Controls;

/// <summary>
/// Find &amp; replace strip for a AvaloniaEdit <see cref="TextEditor"/>, built in code so
/// every SQL surface in the app can share it. Find walks matches with Enter / F3,
/// highlights them all, and replace goes through the document so Ctrl+Z still works.
/// </summary>
public sealed class EditorFindBar
{
    private const int MaxHighlightedMatches = 5000;

    private readonly TextEditor _editor;
    private readonly Border _root;
    private readonly TextBox _find = new() { Width = 190, PlaceholderText = "Find" };
    private readonly TextBox _replace = new() { Width = 160, PlaceholderText = "Replace with" };
    private readonly CheckBox _matchCase = new() { Content = "Aa" };
    private readonly CheckBox _wholeWord = new() { Content = "W" };
    private readonly TextBlock _count = new() { FontSize = 11, MinWidth = 74, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _prev = new() { Content = "▲", Padding = new Thickness(7, 2) };
    private readonly Button _next = new() { Content = "▼", Padding = new Thickness(7, 2) };
    private readonly Button _replaceOne = new() { Content = "Replace", Padding = new Thickness(9, 2) };
    private readonly Button _replaceAll = new() { Content = "All", Padding = new Thickness(7, 2) };
    private readonly Button _close = new() { Content = "✕", Padding = new Thickness(7, 2) };
    private readonly SqlMatchHighlightRenderer _renderer = new();

    private readonly List<TextLocation> _matches = new();
    private int _current = -1;
    private bool _substituteOnTextChange;

    public EditorFindBar(TextEditor editor)
    {
        _editor = editor;
        _editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);
        ToolTip.SetTip(_matchCase, "Match case");
        ToolTip.SetTip(_wholeWord, "Whole word only");

        _find.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
            if (e.Key == Key.Enter) { Navigate(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        _replace.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ReplaceCurrent(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        _find.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && !_substituteOnTextChange) Rebuild();
        };
        _matchCase.PropertyChanged += (_, _) => Rebuild();
        _wholeWord.PropertyChanged += (_, _) => Rebuild();
        _next.Click += (_, _) => Navigate(1);
        _prev.Click += (_, _) => Navigate(-1);
        _replaceOne.Click += (_, _) => ReplaceCurrent();
        _replaceAll.Click += (_, _) => ReplaceAll();
        _close.Click += (_, _) => Close();

        _root = new Border
        {
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            CornerRadius = new CornerRadius(0, 0, 6, 6),
            BorderThickness = new Thickness(1, 0, 1, 1),
            Padding = new Thickness(8, 6),
            Background = new SolidColorBrush(Color.Parse("#241F31")),
            BorderBrush = new SolidColorBrush(Color.Parse("#7C6F9B")),
            Child = BuildLayout()
        };
    }

    /// <summary>The strip itself — add it on top of the editor in the visual tree.</summary>
    public Control Host => _root;

    public bool IsOpen => _root.IsVisible;

    public int MatchCount => _matches.Count;

    public string StatusText => _count.Text ?? string.Empty;

    private Control BuildLayout()
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(6, 0, 0, 0)
        };
        buttons.Children.Add(_prev);
        buttons.Children.Add(_next);
        buttons.Children.Add(_replaceOne);
        buttons.Children.Add(_replaceAll);
        buttons.Children.Add(_close);

        var findRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        findRow.Children.Add(_find);
        findRow.Children.Add(_matchCase);
        findRow.Children.Add(_wholeWord);
        findRow.Children.Add(_count);

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(findRow);
        panel.Children.Add(_replace);
        panel.Children.Add(buttons);
        return panel;
    }

    /// <summary>Show the strip; <paramref name="withReplace"/> also focuses the replace box.</summary>
    public void Open(bool withReplace)
    {
        _root.IsVisible = true;
        var selected = _editor.SelectionLength > 0 ? _editor.SelectedText : null;
        if (!string.IsNullOrEmpty(selected) && selected!.Length <= 200 && !selected.Contains('\n'))
        {
            _substituteOnTextChange = true;
            _find.Text = selected;
            _substituteOnTextChange = false;
        }
        Rebuild();
        (withReplace ? _replace : _find).Focus();
        if (withReplace) _replace.SelectAll();
        else _find.SelectAll();
    }

    public void Close()
    {
        _root.IsVisible = false;
        _matches.Clear();
        _current = -1;
        _renderer.Matches = Array.Empty<TextLocation>();
        _renderer.SelectedIndex = -1;
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        _editor.TextArea.Focus();
    }

    /// <summary>Ctrl+F / Ctrl+H / F3 / Shift+F3 / Escape while the editor has focus.</summary>
    public bool HandleKeyDown(KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && e.Key == Key.F) { Open(false); return true; }
        if (ctrl && e.Key == Key.H) { Open(true); return true; }
        if (e.Key == Key.F3) { Navigate(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); return true; }
        if (e.Key == Key.Escape && IsOpen) { Close(); return true; }
        return false;
    }

    /// <summary>Re-run the search after the query or the document changed.</summary>
    public void Rebuild()
    {
        var needle = _find.Text ?? string.Empty;
        _matches.Clear();
        _current = -1;
        if (needle.Length > 0)
        {
            var text = _editor.Text ?? string.Empty;
            foreach (var start in EnumerateMatches(text, needle, _matchCase.IsChecked == true, _wholeWord.IsChecked == true))
                _matches.Add(new TextLocation(start, needle.Length));
            if (_matches.Count > 0) _current = FirstAtOrAfter(_editor.CaretOffset);
        }
        Publish();
    }

    private void Navigate(int step)
    {
        if (_matches.Count == 0) { Rebuild(); if (_matches.Count == 0) { Publish("No matches"); return; } }
        _current = (_current + step + _matches.Count) % _matches.Count;
        Publish();
        var loc = _matches[_current];
        _editor.Select(loc.Offset, loc.Length);
        _editor.TextArea.Caret.BringCaretToView();
    }

    private void ReplaceCurrent()
    {
        if (_editor.IsReadOnly) { Publish("Read-only"); return; }
        if (_current < 0 || _current >= _matches.Count) { Navigate(1); return; }
        var loc = _matches[_current];
        _editor.Document?.Replace(loc.Offset, loc.Length, _replace.Text ?? string.Empty);
        Rebuild();
        if (_matches.Count > 0)
        {
            _current = _current % _matches.Count;
            Publish();
            _editor.Select(_matches[_current].Offset, _matches[_current].Length);
        }
        else Publish("No matches");
    }

    private void ReplaceAll()
    {
        if (_editor.IsReadOnly) { Publish("Read-only"); return; }
        if (_matches.Count == 0) { Rebuild(); if (_matches.Count == 0) { Publish("No matches"); return; } }
        var replaced = _matches.Count;
        var with = _replace.Text ?? string.Empty;
        // Backwards keeps earlier offsets valid while the document shrinks or grows.
        for (var i = _matches.Count - 1; i >= 0; i--)
            _editor.Document?.Replace(_matches[i].Offset, _matches[i].Length, with);
        Rebuild();
        Publish($"{replaced} replacement(s)");
    }

    private void Publish() => Publish(null);

    private void Publish(string? overrideText)
    {
        _count.Text = overrideText ?? (_matches.Count == 0
            ? (string.IsNullOrEmpty(_find.Text) ? "" : "No matches")
            : $"{_current + 1} of {_matches.Count:N0}");
        var shown = _matches.Count <= MaxHighlightedMatches ? _matches : new List<TextLocation>();
        _renderer.Matches = shown;
        _renderer.SelectedIndex = _matches.Count <= MaxHighlightedMatches ? _current : -1;
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private int FirstAtOrAfter(int offset)
    {
        for (var i = 0; i < _matches.Count; i++)
            if (_matches[i].Offset >= offset) return i;
        return _matches.Count == 0 ? -1 : 0;
    }

    internal static IEnumerable<int> EnumerateMatches(string text, string needle, bool matchCase, bool wholeWord)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var at = 0;
        while (at <= text.Length - needle.Length)
        {
            var found = text.IndexOf(needle, at, comparison);
            if (found < 0) yield break;
            if (!wholeWord || IsWholeWord(text, found, needle.Length)) yield return found;
            at = found + Math.Max(1, needle.Length);
        }
    }

    private static bool IsWholeWord(string text, int start, int length)
    {
        var before = start - 1;
        if (before >= 0 && IsWordChar(text[before])) return false;
        var after = start + length;
        return after >= text.Length || !IsWordChar(text[after]);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Offset + length of one search hit.</summary>
    public readonly record struct TextLocation(int Offset, int Length);
}

/// <summary>Paints search hits (amber) and the active hit (blue) behind the text.</summary>
internal sealed class SqlMatchHighlightRenderer : IBackgroundRenderer
{
    public IReadOnlyList<EditorFindBar.TextLocation> Matches { get; set; } = Array.Empty<EditorFindBar.TextLocation>();
    public int SelectedIndex { get; set; } = -1;
    public KnownLayer Layer => KnownLayer.Background;

    private static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#4FC9A22B"));
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#803B82F6"));

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var document = textView.Document;
        if (document == null || Matches.Count == 0 || textView.VisualLines.Count == 0) return;

        for (var i = 0; i < Matches.Count; i++)
        {
            var match = Matches[i];
            var start = Math.Clamp(match.Offset, 0, document.TextLength);
            var end = Math.Clamp(start + match.Length, start, document.TextLength);
            if (start >= end) continue;
            var brush = i == SelectedIndex ? Active : Dim;
            var segment = new TextSegment { StartOffset = start, Length = end - start };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                drawingContext.FillRectangle(brush, new Rect(rect.X - 1, rect.Y, rect.Width + 2, rect.Height));
        }
    }
}
