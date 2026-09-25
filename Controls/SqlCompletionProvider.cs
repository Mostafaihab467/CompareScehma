using System.Text;
using System.Text.RegularExpressions;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Avalonia.Input;
using SchemaCompare.Services;

namespace SchemaCompare.Controls;

/// <summary>
/// SSMS-style IntelliSense completion for T-SQL. Candidates are ranked:
/// exact > prefix > word-boundary > flat prefix > contains > subsequence >
/// fuzzy (edit distance), with context boosts (tables after FROM/JOIN,
/// columns of referenced tables after SELECT/WHERE/ON). The list is rebuilt
/// and re-ranked on every keystroke; AvaloniaEdit's built-in prefix filter is
/// disabled so nearest/fuzzy matches stay visible.
/// </summary>
public static class SqlCompletionProvider
{
    private static readonly string[] Keywords =
    [
        "SELECT", "FROM", "WHERE", "JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN",
        "INNER JOIN", "CROSS JOIN", "ON", "AND", "OR", "NOT", "IN", "EXISTS",
        "BETWEEN", "LIKE", "IS NULL", "IS NOT NULL", "GROUP BY", "HAVING",
        "ORDER BY", "ASC", "DESC", "TOP", "DISTINCT", "AS", "CASE", "WHEN",
        "THEN", "ELSE", "END", "UNION", "UNION ALL", "INSERT INTO", "VALUES",
        "UPDATE", "SET", "DELETE FROM", "CREATE TABLE", "ALTER TABLE", "DROP TABLE",
        "CREATE VIEW", "CREATE PROCEDURE", "EXEC", "EXECUTE", "BEGIN", "COMMIT",
        "ROLLBACK", "TRANSACTION", "DECLARE", "GO", "USE", "WITH", "OVER",
        "PARTITION BY", "PRIMARY KEY", "FOREIGN KEY", "CONSTRAINT", "DEFAULT",
        "IDENTITY", "NULL", "NOT NULL"
    ];

    private static readonly string[] Functions =
    [
        "COUNT", "SUM", "AVG", "MIN", "MAX", "GETDATE()", "GETUTCDATE()",
        "ISNULL", "COALESCE", "CAST", "CONVERT", "LEN", "SUBSTRING",
        "CHARINDEX", "LEFT", "RIGHT", "LTRIM", "RTRIM", "UPPER", "LOWER",
        "REPLACE", "DATEADD", "DATEDIFF", "YEAR", "MONTH", "DAY",
        "NEWID()", "DB_NAME()", "@@ROWCOUNT", "@@SERVERNAME", "@@VERSION"
    ];

    private static readonly string[] Types =
    [
        "INT", "BIGINT", "SMALLINT", "TINYINT", "BIT", "DECIMAL", "NUMERIC",
        "MONEY", "FLOAT", "REAL", "CHAR", "VARCHAR", "NCHAR", "NVARCHAR",
        "TEXT", "NTEXT", "DATE", "DATETIME", "DATETIME2", "SMALLDATETIME",
        "UNIQUEIDENTIFIER"
    ];

    private static readonly string[] ExtraStopWords =
        ["CROSS", "OUTER", "INNER", "RIGHT", "EXCEPT", "INTERSECT", "PIVOT", "UNPIVOT"];

    /// <summary>Words that must not be mistaken for a table alias.</summary>
    private static readonly HashSet<string> KeywordSet = new(
        Keywords.Concat(ExtraStopWords), StringComparer.OrdinalIgnoreCase);

    /// <summary>Table names are boosted right after these keywords.</summary>
    private static readonly HashSet<string> TableContextKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER",
        "INTO", "UPDATE", "TABLE", "VIEW", "REFERENCES", "DELETE", "TRUNCATE"
    };

    /// <summary>Columns are boosted right after these keywords.</summary>
    private static readonly HashSet<string> ColumnContextKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "WHERE", "ON", "SET", "BY", "HAVING", "AND", "OR",
        "ORDER", "GROUP", "VALUES", "WHEN", "THEN", "ELSE", "DISTINCT", "TOP"
    };

    // FROM/JOIN/INTO/UPDATE followed by a (possibly schema-qualified) identifier.
    private static readonly Regex ReferencedTableRegex = new(
        @"\b(?:FROM|JOIN|INTO|UPDATE)\s+(?<t>\[?[\w#$]+\]?(?:\s*\.\s*\[?[\w#$]+\]?)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Same, plus a following alias: "FROM Orders o" / "JOIN x AS y".
    private static readonly Regex AliasRegex = new(
        @"\b(?:FROM|JOIN)\s+(?<t>\[?[\w#$]+\]?(?:\s*\.\s*\[?[\w#$]+\]?)?)\s+(?:AS\s+)?(?<a>\[?[\w#$]+\]?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Raised after Tables / ColumnsByTable are refreshed so open editors re-lint.</summary>
    public static event EventHandler? SchemaChanged;

    public static void NotifySchemaChanged() => SchemaChanged?.Invoke(null, EventArgs.Empty);
    public static List<QuerySchemaService.TableInfo> Tables { get; set; } = [];
    public static Dictionary<string, List<string>> ColumnsByTable { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    private static CompletionWindow? _window;

    /// <summary>True while the completion popup is open (editors hide hover tips).</summary>
    public static bool IsPopupOpen => _window != null;

    public static void Attach(TextEditor editor)
    {
        editor.TextArea.TextEntered -= OnTextEntered;
        editor.TextArea.TextEntered += OnTextEntered;
    }

    public static void Detach(TextEditor editor)
    {
        editor.TextArea.TextEntered -= OnTextEntered;
    }

    public static void Close()
    {
        _window?.Close();
        _window = null;
    }

    private static void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextArea area || area.Document == null)
            return;
        if (string.IsNullOrEmpty(e.Text))
            return;
        if (e.Text == "\b")
        {
            // Backspace: re-rank the open list (if any) against the shorter word.
            if (_window != null)
                Show(area, area.Caret.Offset);
            return;
        }
        var ch = e.Text[0];
        // Trigger on word chars and dot; dot shows columns of the preceding table.
        if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.')
            return;
        Show(area, area.Caret.Offset);
    }

    /// <summary>Shows the completion list anchored at the current word.</summary>
    public static void Show(TextArea area, int caretOffset)
    {
        Close();
        var doc = area.Document;
        if (doc == null) return;

        var (wordStart, word) = GetCurrentWord(doc, caretOffset);
        var dotContext = GetDotContext(doc, wordStart);
        var prevKeyword = GetPreviousKeyword(doc, wordStart);
        var tableContext = TableContextKeywords.Contains(prevKeyword);
        var columnContext = ColumnContextKeywords.Contains(prevKeyword);

        var candidates = new Dictionary<string, (string Text, string Kind, int Score)>(
            StringComparer.OrdinalIgnoreCase);

        void Offer(string text, string kind, int bonus)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var match = MatchScore(text, word);
            if (match < 0) return;
            var score = match + bonus;
            if (candidates.TryGetValue(text, out var existing) && existing.Score >= score)
                return;
            candidates[text] = (text, kind, score);
        }

        if (dotContext != null)
        {
            // "t.|" -> columns of the aliased/schema-qualified table.
            var columns = ResolveColumns(doc.Text, dotContext);
            if (columns.Count > 0)
            {
                foreach (var col in columns) Offer(col, "column", 150);
            }
            else
            {
                foreach (var t in Tables)
                {
                    Offer(t.Name, "table", 60);
                    Offer(t.ShortName, "table", 55);
                }
            }
        }
        else
        {
            var tableBonus = tableContext ? 500 : 0;
            var columnBonus = columnContext ? 400 : 0;

            foreach (var k in Keywords) Offer(k, "keyword", 120);
            foreach (var f in Functions) Offer(f, "function", 100);
            foreach (var t in Types) Offer(t, "type", 80);
            foreach (var t in Tables)
            {
                Offer(t.Name, "table", 60 + tableBonus);
                Offer(t.ShortName, "table", 55 + tableBonus);
            }

            // Columns of tables referenced in this script (FROM/JOIN/INTO/UPDATE).
            foreach (var key in GetReferencedTables(doc.Text))
            {
                if (!ColumnsByTable.TryGetValue(key, out var cols)) continue;
                foreach (var c in cols) Offer(c, "column", 40 + columnBonus);
            }
        }

        if (candidates.Count == 0)
            return; // nothing relevant - don't pop up

        var window = new CompletionWindow(area)
        {
            CloseAutomatically = true,
            CloseWhenCaretAtBeginning = true,
            // Replacement segment must span the WHOLE word typed so far, not just
            // from the caret — otherwise accepting (Enter/Tab) keeps the already-
            // typed prefix and yields e.g. "s" + "SELECT" = "sSELECT".
            StartOffset = wordStart,
            EndOffset = caretOffset
        };
        // We rank candidates ourselves (nearest/fuzzy included); AvaloniaEdit's
        // built-in prefix filter would hide non-prefix matches.
        window.CompletionList.IsFiltering = false;

        var data = window.CompletionList.CompletionData;
        ICompletionData? first = null;
        foreach (var c in candidates.Values
                     .OrderByDescending(c => c.Score)
                     .ThenBy(c => c.Text.Length)
                     .ThenBy(c => c.Text, StringComparer.OrdinalIgnoreCase)
                     .Take(80))
        {
            var item = new SqlCompletionData(c.Text, c.Kind);
            data.Add(item);
            first ??= item;
        }
        if (first == null)
            return;
        window.CompletionList.SelectedItem = first;

        _window = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
                _window = null;
        };
        window.Show();
    }

    private static (int Start, string Word) GetCurrentWord(TextDocument doc, int offset)
    {
        offset = Math.Clamp(offset, 0, doc.TextLength);
        var start = offset;
        while (start > 0)
        {
            var c = doc.GetCharAt(start - 1);
            if (char.IsLetterOrDigit(c) || c == '_' || c == '#')
                start--;
            else
                break;
        }
        return (start, doc.GetText(start, offset - start));
    }

    /// <summary>Identifier before a trailing dot (e.g. "u" in "u.|"), or null.</summary>
    private static string? GetDotContext(TextDocument doc, int wordStart)
    {
        var i = wordStart - 1;
        while (i >= 0 && char.IsWhiteSpace(doc.GetCharAt(i))) i--;
        if (i < 0 || doc.GetCharAt(i) != '.')
            return null;
        i--;
        while (i >= 0 && char.IsWhiteSpace(doc.GetCharAt(i))) i--;
        var end = i + 1;
        while (i >= 0)
        {
            var c = doc.GetCharAt(i);
            if (char.IsLetterOrDigit(c) || c == '_' || c == '#')
                i--;
            else
                break;
        }
        var token = doc.GetText(i + 1, end - (i + 1));
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    /// <summary>Keyword immediately before the current word (upper-case), or "".</summary>
    private static string GetPreviousKeyword(TextDocument doc, int wordStart)
    {
        var i = wordStart - 1;
        while (i >= 0 && char.IsWhiteSpace(doc.GetCharAt(i))) i--;
        var end = i + 1;
        while (i >= 0)
        {
            var c = doc.GetCharAt(i);
            if (char.IsLetterOrDigit(c) || c == '_') i--;
            else break;
        }
        if (i + 1 >= end) return string.Empty;
        return doc.GetText(i + 1, end - (i + 1)).ToUpperInvariant();
    }

    /// <summary>Columns for a dot-context token: table name, schema-qualified
    /// name, or an alias declared via FROM/JOIN (with optional AS).</summary>
    private static List<string> ResolveColumns(string docText, string token)
    {
        foreach (var t in Tables)
        {
            if (string.Equals(t.Name, token, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.ShortName, token, StringComparison.OrdinalIgnoreCase))
            {
                if (ColumnsByTable.TryGetValue($"{t.Schema}.{t.Name}", out var cols))
                    return cols;
            }
        }
        foreach (Match m in AliasRegex.Matches(docText))
        {
            var alias = m.Groups["a"].Value.Trim().Trim('[', ']');
            if (string.Equals(alias, token, StringComparison.OrdinalIgnoreCase))
            {
                var key = ResolveTableKey(m.Groups["t"].Value);
                if (key != null && ColumnsByTable.TryGetValue(key, out var cols))
                    return cols;
            }
        }
        return [];
    }

    /// <summary>Schema-qualified keys ("schema.Name") of tables referenced by
    /// FROM/JOIN/INTO/UPDATE in the current script.</summary>
    private static IEnumerable<string> GetReferencedTables(string text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in ReferencedTableRegex.Matches(text))
        {
            var key = ResolveTableKey(m.Groups["t"].Value);
            if (key != null) found.Add(key);
        }
        return found;
    }

    /// <summary>Resolves "Orders" / "dbo.Orders" / "[dbo].[Orders]" to the
    /// "schema.Name" key used by <see cref="ColumnsByTable"/>.</summary>
    private static string? ResolveTableKey(string raw)
    {
        var name = raw.Trim();
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..];
        name = name.Trim().Trim('[', ']').Trim();
        if (name.Length == 0) return null;
        foreach (var t in Tables)
            if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                return $"{t.Schema}.{t.Name}";
        // Unknown to metadata (e.g. temp table) - fall back to the columns map.
        foreach (var key in ColumnsByTable.Keys)
            if (key.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                return key;
        return null;
    }

    /// <summary>
    /// Ranks <paramref name="text"/> against the typed <paramref name="word"/>:
    /// 800 exact, 600 prefix, 500 word-boundary, 400 flat prefix (inside e.g.
    /// GETDATE()), 300 contains, 200 subsequence, fuzzy up to 190 by edit
    /// distance; empty word matches everything with 0. -1 = no match.
    /// </summary>
    private static int MatchScore(string text, string word)
    {
        if (string.IsNullOrEmpty(word)) return 0;
        if (word.Length > text.Length + 3) return -1;

        if (string.Equals(text, word, StringComparison.OrdinalIgnoreCase)) return 800;
        if (text.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return 600;

        var idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (idx > 0)
        {
            var before = text[idx - 1];
            if (!char.IsLetterOrDigit(before) && before != '_')
                return 500; // keyword boundary: "SELECT" in "DELETE FROM" etc.
            idx = text.IndexOf(word, idx + 1, StringComparison.OrdinalIgnoreCase);
        }

        var flat = Flatten(text);
        if (flat.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return 400;
        if (flat.Contains(word, StringComparison.OrdinalIgnoreCase)) return 300;
        if (IsSubsequence(word, flat)) return 200;

        var maxDist = word.Length <= 4 ? 1 : word.Length <= 8 ? 2 : 3;
        if (Levenshtein(word, flat, maxDist) <= maxDist) return 190;
        return -1;
    }

    /// <summary>Uppercases and strips non-alphanumerics: GETDATE() -> GETDATE.</summary>
    private static string Flatten(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    /// <summary>True when every char of word appears in order inside flat.</summary>
    private static bool IsSubsequence(string word, string flat)
    {
        var i = 0;
        foreach (var c in flat)
        {
            if (char.ToUpperInvariant(c) == char.ToUpperInvariant(word[i]) && ++i == word.Length)
                return true;
        }
        return false;
    }

    /// <summary>Bounded Levenshtein: aborts once the best possible score
    /// exceeds <paramref name="maxDist"/> (returns maxDist + 1).</summary>
    private static int Levenshtein(string a, string b, int maxDist)
    {
        if (Math.Abs(a.Length - b.Length) > maxDist) return maxDist + 1;
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var best = curr[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                if (curr[j] < best) best = curr[j];
            }
            if (best > maxDist) return maxDist + 1;
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    private sealed class SqlCompletionData(string text, string kind) : ICompletionData
    {
        public Avalonia.Media.IImage? Image => null;
        public string Text { get; } = text;
        public object Content => Text;
        public object Description => kind;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document?.Replace(completionSegment, Text);
        }
    }
}



