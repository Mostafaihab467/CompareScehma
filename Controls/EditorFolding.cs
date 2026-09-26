using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace SchemaCompare.Controls;

/// <summary>
/// Collapsible regions for a AvaloniaEdit <see cref="TextEditor"/>: block comments,
/// T-SQL BEGIN…END / BEGIN TRANSACTION blocks, GO-separated batches and multi-line
/// parenthesised expressions. Folding is view state only — the document is never
/// mutated, so Text stays complete for saving and Ctrl+F keeps its own renderer layer.
/// </summary>
public sealed class EditorFolding
{
    private readonly TextEditor _editor;
    private readonly FoldingManager _manager;

    public EditorFolding(TextEditor editor)
    {
        _editor = editor;
        _manager = FoldingManager.Install(editor.TextArea);
        _editor.TextChanged += OnTextChanged;
        Rebuild();
    }

    /// <summary>Sections the manager currently knows about, innermost state included.</summary>
    public IReadOnlyList<FoldingSection> Sections => _manager.AllFoldings.ToList();

    public int SectionCount => _manager.AllFoldings.Count();

    public int FoldedCount => _manager.AllFoldings.Count(f => f.IsFolded);

    /// <summary>Ctrl+M toggle · Ctrl+Shift+M fold all · Ctrl+Alt+M unfold all.</summary>
    public bool HandleKeyDown(KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Key != Key.M) return false;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { FoldAll(); return true; }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { UnfoldAll(); return true; }
        return ToggleCurrent();
    }

    /// <summary>Folds or unfolds the innermost region containing the caret.</summary>
    public bool ToggleCurrent()
    {
        var doc = _editor.Document;
        if (doc == null || doc.TextLength == 0) return false;
        var caret = Math.Clamp(_editor.CaretOffset, 0, doc.TextLength);
        var line = doc.GetLineByOffset(caret);
        var section = _manager.GetFoldingsContaining(caret)
            .Concat(_manager.GetFoldingsContaining(line.Offset))
            .Where(f => f.StartOffset < f.EndOffset)
            .OrderBy(f => f.EndOffset - f.StartOffset)
            .ThenBy(f => f.StartOffset)
            .FirstOrDefault();
        if (section == null) return false;
        section.IsFolded = !section.IsFolded;
        return true;
    }

    public int FoldAll()
    {
        var n = 0;
        foreach (var f in _manager.AllFoldings)
            if (!f.IsFolded) { f.IsFolded = true; n++; }
        return n;
    }

    public int UnfoldAll()
    {
        var n = 0;
        foreach (var f in _manager.AllFoldings)
            if (f.IsFolded) { f.IsFolded = false; n++; }
        return n;
    }

    /// <summary>Recompute the region list from the current text; fold state survives by start offset.</summary>
    public void Rebuild()
    {
        try
        {
            _manager.UpdateFoldings(ComputeFoldings(_editor.Text ?? string.Empty), int.MaxValue);
        }
        catch
        {
            /* folding must never break editing */
        }
    }

    public void Detach()
    {
        _editor.TextChanged -= OnTextChanged;
        FoldingManager.Uninstall(_manager);
    }

    private void OnTextChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>
    /// Single-pass T-SQL scanner (skips strings, brackets, line and block comments)
    /// producing non-crossing multi-line fold spans. Offsets are 0-based into
    /// <paramref name="text"/>; spans cover whole lines so collapsing never splits a line.
    /// </summary>
    internal static List<NewFolding> ComputeFoldings(string text)
    {
        var spans = new List<(int Start, int End, string Kind)>();
        if (text.Length == 0) return new List<NewFolding>();

        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') lineStarts.Add(i + 1);

        int LineIndex(int offset)
        {
            var idx = lineStarts.BinarySearch(Math.Min(offset, text.Length));
            return idx >= 0 ? idx : Math.Max(~idx - 1, 0);
        }
        int LineStartOf(int offset) => lineStarts[LineIndex(offset)];
        // A folding must end at or before its line's delimiter start (DocumentLine.EndOffset).
        // Ending on the '\n' of a CRLF line lands inside the delimiter and AvaloniaEdit throws
        // "produced an element which ends within the line delimiter" on the next layout pass.
        int LineEndOf(int offset)
        {
            var li = LineIndex(offset);
            if (li + 1 >= lineStarts.Count) return text.Length;
            var next = lineStarts[li + 1];
            while (next > lineStarts[li] && (text[next - 1] == '\n' || text[next - 1] == '\r')) next--;
            return next;
        }
        void AddSpan(int firstLineOffset, int lastLineOffset, string kind)
        {
            if (lastLineOffset < 0 || lastLineOffset > text.Length) return;
            if (LineIndex(lastLineOffset) <= LineIndex(firstLineOffset)) return;
            spans.Add((LineStartOf(firstLineOffset), LineEndOf(lastLineOffset), kind));
        }

        var beginStack = new Stack<(int Start, string Kind)>();
        var parenStack = new Stack<int>();
        var batchStart = 0;
        var sawGo = false;
        var at = 0;
        while (at < text.Length)
        {
            var c = text[at];
            if (c == '-' && at + 1 < text.Length && text[at + 1] == '-')
            {
                at = NextLineOffset(text, at);
                continue;
            }
            if (c == '/' && at + 1 < text.Length && text[at + 1] == '*')
            {
                var open = at;
                var close = text.IndexOf("*/", open + 2, StringComparison.Ordinal);
                var end = close < 0 ? text.Length : close + 2;
                AddSpan(open, end - 1, "comment");
                at = end;
                continue;
            }
            if (c == '\'' || c == '"')
            {
                at = SkipQuoted(text, at, c);
                continue;
            }
            if (c == '[')
            {
                at = SkipBracket(text, at);
                continue;
            }
            if (c == '(')
            {
                parenStack.Push(at);
                at++;
                continue;
            }
            if (c == ')')
            {
                if (parenStack.Count > 0) AddSpan(parenStack.Pop(), at, "paren");
                at++;
                continue;
            }
            if (IsWordStart(c))
            {
                var wordEnd = at;
                while (wordEnd < text.Length && IsWordChar(text[wordEnd])) wordEnd++;
                var word = text.Substring(at, wordEnd - at).ToUpperInvariant();
                switch (word)
                {
                    case "BEGIN":
                        beginStack.Push((LineStartOf(at),
                            IsTranStart(text, wordEnd) ? "tran" : "begin"));
                        break;
                    case "END":
                        if (beginStack.Count > 0)
                        {
                            var (start, kind) = beginStack.Pop();
                            AddSpan(start, wordEnd - 1, kind);
                        }
                        break;
                    case "COMMIT":
                    case "ROLLBACK":
                        if (beginStack.Count > 0 && beginStack.Peek().Kind == "tran")
                        {
                            var (start, kind) = beginStack.Pop();
                            AddSpan(start, wordEnd - 1, kind);
                        }
                        break;
                    case "GO":
                        if (IsAloneOnLine(text, at, wordEnd))
                        {
                            sawGo = true;
                            var goLineStart = LineStartOf(at);
                            if (goLineStart > batchStart) AddSpan(batchStart, goLineStart - 2, "batch");
                            batchStart = Math.Min(NextLineOffset(text, wordEnd - 1), text.Length);
                        }
                        break;
                }
                at = wordEnd;
                continue;
            }
            at++;
        }
        if (sawGo && batchStart < text.Length) AddSpan(batchStart, text.Length - 1, "batch");

        // Keep only non-crossing, non-duplicate spans so the FoldingManager segment
        // tree stays well-formed; enclosing spans win over ones that straddle them.
        var ordered = spans
            .OrderBy(s => s.Start)
            .ThenByDescending(s => s.End)
            .ToList();
        var kept = new List<(int Start, int End, string Kind)>();
        var seen = new HashSet<(int, int)>();
        var enclosingEnds = new Stack<int>();
        foreach (var s in ordered)
        {
            while (enclosingEnds.Count > 0 && enclosingEnds.Peek() < s.Start) enclosingEnds.Pop();
            if (enclosingEnds.Count > 0 && s.End > enclosingEnds.Peek()) continue;
            if (!seen.Add((s.Start, s.End))) continue;
            kept.Add(s);
            enclosingEnds.Push(s.End);
        }

        return kept.Select(s => new NewFolding
        {
            StartOffset = s.Start,
            EndOffset = s.End,
            Name = $"{Label(s.Kind)} ({LineIndex(s.End) - LineIndex(s.Start) + 1} lines)",
            DefaultClosed = false
        }).ToList();
    }

    private static string Label(string kind) => kind switch
    {
        "comment" => "/* ... */",
        "begin" => "BEGIN ... END",
        "tran" => "BEGIN TRANSACTION ... COMMIT/ROLLBACK",
        "batch" => "GO batch",
        _ => "( ... )"
    };

    private static int NextLineOffset(string text, int from)
    {
        var nl = text.IndexOf('\n', from);
        return nl < 0 ? text.Length : nl + 1;
    }

    private static int SkipQuoted(string text, int open, char quote)
    {
        var i = open + 1;
        while (i < text.Length)
        {
            if (text[i] == quote)
            {
                if (i + 1 < text.Length && text[i + 1] == quote) { i += 2; continue; }
                return i + 1;
            }
            i++;
        }
        return text.Length;
    }

    private static int SkipBracket(string text, int open)
    {
        var i = open + 1;
        while (i < text.Length && text[i] != ']')
        {
            if (text[i] == '\n') return i;
            i++;
        }
        return Math.Min(i + 1, text.Length);
    }

    private static bool IsTranStart(string text, int afterBegin)
    {
        var i = afterBegin;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        var end = i;
        while (end < text.Length && IsWordChar(text[end])) end++;
        var word = text.Substring(i, end - i).ToUpperInvariant();
        return word == "TRAN" || word == "TRANSACTION";
    }

    private static bool IsAloneOnLine(string text, int wordStart, int wordEnd)
    {
        var ls = wordStart;
        while (ls > 0 && text[ls - 1] != '\n') ls--;
        for (var i = ls; i < wordStart; i++)
            if (!char.IsWhiteSpace(text[i])) return false;
        var i2 = wordEnd;
        while (i2 < text.Length && char.IsWhiteSpace(text[i2])) i2++;
        if (i2 < text.Length && text[i2] == ';') { i2++; while (i2 < text.Length && char.IsWhiteSpace(text[i2])) i2++; }
        if (i2 >= text.Length || text[i2] == '\n' || text[i2] == '\r') return true;
        return text[i2] == '-' && i2 + 1 < text.Length && text[i2 + 1] == '-';
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_' || c == '@' || c == '#';

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#';
}
