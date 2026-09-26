using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace SchemaCompare.Controls;

/// <summary>
/// SSMS-style bookmarks for a AvaloniaEdit <see cref="TextEditor"/>. AvaloniaEdit
/// 12.0 ships no ITextMarker/TextMarkerService, so this class owns the bookmarked
/// lines itself (stored as document line-start offsets and remapped on document
/// changes so they survive typing above them) and paints a glyph through a custom
/// left margin.
/// </summary>
public sealed class EditorBookmarks
{
    private readonly TextEditor _editor;
    private readonly BookmarkMargin _margin;
    private readonly HashSet<int> _lineStartOffsets = new();
    private TextDocument? _hookedDocument;

    public EditorBookmarks(TextEditor editor)
    {
        _editor = editor;
        _margin = new BookmarkMargin(this);
        editor.TextArea.LeftMargins.Insert(0, _margin);
        editor.TextArea.TextView.VisualLinesChanged += OnVisualLinesChanged;
        editor.PropertyChanged += OnEditorPropertyChanged;
        HookDocument(editor.Document);
    }

    public int Count => _lineStartOffsets.Count;

    /// <summary>1-based line numbers that hold a bookmark, ascending.</summary>
    public IReadOnlyList<int> BookmarkedLines
    {
        get
        {
            var doc = _editor.Document;
            if (doc == null) return Array.Empty<int>();
            return _lineStartOffsets
                .Select(o => doc.GetLineByOffset(Math.Min(o, doc.TextLength)).LineNumber)
                .OrderBy(n => n)
                .ToList();
        }
    }

    public bool HasBookmark(int lineNumber)
    {
        var doc = _editor.Document;
        if (doc == null || lineNumber < 1 || lineNumber > doc.LineCount) return false;
        return _lineStartOffsets.Contains(doc.GetLineByNumber(lineNumber).Offset);
    }

    /// <summary>Ctrl+B toggle · Ctrl+K next · Ctrl+Shift+K previous · Ctrl+Shift+B clear all.</summary>
    public bool HandleKeyDown(KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return false;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.B)
        {
            if (shift) { ClearAll(); return true; }
            return ToggleCurrent();
        }
        if (e.Key == Key.K && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return shift ? Previous() : Next();
        }
        return false;
    }

    /// <summary>Add or remove the bookmark on the caret line; true when one was added.</summary>
    public bool ToggleCurrent()
    {
        var doc = _editor.Document;
        if (doc == null) return false;
        var line = doc.GetLineByNumber(Math.Clamp(_editor.TextArea.Caret.Line, 1, doc.LineCount));
        if (!_lineStartOffsets.Add(line.Offset)) _lineStartOffsets.Remove(line.Offset);
        _margin.InvalidateVisual();
        return true;
    }

    public int ClearAll()
    {
        var n = _lineStartOffsets.Count;
        _lineStartOffsets.Clear();
        _margin.InvalidateVisual();
        return n;
    }

    public bool Next() => Move(+1);

    public bool Previous() => Move(-1);

    /// <summary>Jumps the caret to (and scrolls the editor to) a bookmarked line; false when none matched.</summary>
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

    public void Detach()
    {
        HookDocument(null);
        _editor.PropertyChanged -= OnEditorPropertyChanged;
        _editor.TextArea.TextView.VisualLinesChanged -= OnVisualLinesChanged;
        _editor.TextArea.LeftMargins.Remove(_margin);
    }

    private bool Move(int step)
    {
        var doc = _editor.Document;
        if (doc == null || _lineStartOffsets.Count == 0) return false;
        var caretOffset = doc.GetLineByNumber(Math.Clamp(_editor.TextArea.Caret.Line, 1, doc.LineCount)).Offset;
        var sorted = _lineStartOffsets.OrderBy(o => o).ToList();
        var idx = step > 0
            ? sorted.FindIndex(o => o > caretOffset)
            : sorted.FindLastIndex(o => o < caretOffset);
        if (idx < 0) idx = step > 0 ? 0 : sorted.Count - 1; // wrap around the document
        return GoToLine(doc.GetLineByOffset(sorted[idx]).LineNumber);
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => _margin.InvalidateVisual();

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextEditor.DocumentProperty)
            HookDocument(_editor.Document);
    }

    private void HookDocument(TextDocument? next)
    {
        if (ReferenceEquals(_hookedDocument, next)) return;
        if (_hookedDocument != null) _hookedDocument.Changed -= OnDocumentChanged;
        _hookedDocument = next;
        if (next != null) next.Changed += OnDocumentChanged;
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        var doc = _hookedDocument;
        if (doc == null || _lineStartOffsets.Count == 0) return;
        var moved = new HashSet<int>();
        foreach (var offset in _lineStartOffsets)
        {
            var no = e.GetNewOffset(Math.Min(offset, doc.TextLength), AnchorMovementType.Default);
            if (no > doc.TextLength) continue; // bookmark deleted with its text
            moved.Add(doc.GetLineByOffset(no).Offset);
        }
        _lineStartOffsets.Clear();
        _lineStartOffsets.UnionWith(moved);
        _margin.InvalidateVisual();
    }

    internal bool IsBookmarkedLineStart(int offset) => _lineStartOffsets.Contains(offset);

    private sealed class BookmarkMargin : AbstractMargin
    {
        private static readonly IBrush Glyph = new SolidColorBrush(Color.Parse("#F59E0B"));
        private readonly EditorBookmarks _owner;

        public BookmarkMargin(EditorBookmarks owner) => _owner = owner;

        protected override Size MeasureOverride(Size availableSize) => new(16, 0);

        public override void Render(DrawingContext drawingContext)
        {
            var textView = TextView;
            if (textView?.Document == null || _owner.Count == 0 || textView.VisualLines.Count == 0) return;
            foreach (var vl in textView.VisualLines)
            {
                if (!_owner.IsBookmarkedLineStart(vl.FirstDocumentLine.Offset)) continue;
                var top = vl.VisualTop - textView.VerticalOffset;
                if (top + vl.Height < 0 || top > Bounds.Height) continue;
                var cy = top + Math.Min(vl.Height / 2, 10);
                drawingContext.DrawEllipse(Glyph, null, new Point(Bounds.Width / 2, cy), 3.5, 3.5);
            }
        }
    }
}
