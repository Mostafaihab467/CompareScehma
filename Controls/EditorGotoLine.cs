using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Editing;

namespace SchemaCompare.Controls;

/// <summary>
/// Ctrl+G go-to-line overlay for a AvaloniaEdit <see cref="TextEditor"/>, hosted by
/// <see cref="SqlHighlightedEditor"/> the same way <see cref="EditorFindBar"/> is.
/// Entering a number moves the caret to the first column of that line; numbers past
/// the end of the script are clamped to the last line and reported as such.
/// </summary>
public sealed class EditorGotoLine
{
    private readonly TextEditor _editor;
    private readonly Border _root;
    private readonly TextBox _line = new() { Width = 90, PlaceholderText = "Line" };
    private readonly TextBlock _status = new() { FontSize = 11, MinWidth = 90, VerticalAlignment = VerticalAlignment.Center };

    public EditorGotoLine(TextEditor editor)
    {
        _editor = editor;
        _line.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                TryJump();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        _root = new Border
        {
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            CornerRadius = new CornerRadius(6, 0, 0, 0),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            Background = new SolidColorBrush(Color.Parse("#241F31")),
            BorderBrush = new SolidColorBrush(Color.Parse("#7C6F9B")),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { _line, _status }
            }
        };
    }

    /// <summary>The overlay itself — add it on top of the editor in the visual tree.</summary>
    public Control Host => _root;

    public bool IsOpen => _root.IsVisible;

    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>Ctrl+G while the editor has focus; Escape closes.</summary>
    public bool HandleKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.G) { Open(); return true; }
        if (e.Key == Key.Escape && IsOpen) { Close(); return true; }
        return false;
    }

    public void Open()
    {
        _root.IsVisible = true;
        _line.Focus();
        _line.SelectAll();
    }

    public void Close()
    {
        _root.IsVisible = false;
        _editor.TextArea.Focus();
    }

    /// <summary>Parses the box, clamps to the document, jumps; false with a stated reason on bad input.</summary>
    public bool TryJump()
    {
        var doc = _editor.Document;
        var raw = (_line.Text ?? string.Empty).Trim();
        if (doc == null || doc.LineCount == 0)
        {
            _status.Text = "The editor is empty";
            return false;
        }
        if (!TryParseLine(raw, out var requested))
        {
            _status.Text = $"'{raw}' is not a line number";
            return false;
        }
        var line = Math.Clamp(requested, 1, doc.LineCount);
        var ok = GoToLine(line);
        _status.Text = !ok
            ? $"Line {line} is unavailable"
            : line != requested
                ? $"Line {line} (script has {doc.LineCount} lines)"
                : $"Line {line} of {doc.LineCount}";
        if (ok) Close();
        return ok;
    }

    /// <summary>Moves the caret to column 1 of the line and brings it into view.</summary>
    public bool GoToLine(int lineNumber)
    {
        var doc = _editor.Document;
        if (doc == null || lineNumber < 1 || lineNumber > doc.LineCount) return false;
        var line = doc.GetLineByNumber(lineNumber);
        _editor.TextArea.Caret.Position = new TextViewPosition(line.LineNumber, 1);
        _editor.TextArea.Caret.BringCaretToView();
        _editor.Focus();
        return true;
    }

    internal static bool TryParseLine(string raw, out int line) =>
        int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out line) && line >= 1;
}
