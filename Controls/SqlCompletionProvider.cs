using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Avalonia.Input;
using SchemaCompare.Services;

namespace SchemaCompare.Controls;

/// <summary>
/// SSMS-style IntelliSense completion for T-SQL: keywords, functions, tables,
/// views and (after a dot) columns. Attach with <see cref="Attach"/>.
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

    /// <summary>Refreshed on connect; read on the UI thread.</summary>
    public static List<QuerySchemaService.TableInfo> Tables { get; set; } = [];
    public static Dictionary<string, List<string>> ColumnsByTable { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    private static CompletionWindow? _window;

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

        var window = new CompletionWindow(area)
        {
            CloseAutomatically = true,
            CloseWhenCaretAtBeginning = true
        };
        var data = window.CompletionList.CompletionData;

        if (dotContext != null)
        {
            foreach (var col in ResolveColumns(dotContext))
                data.Add(new SqlCompletionData(col, "column", 2));
            if (data.Count == 0)
                foreach (var t in Tables.Take(200))
                    data.Add(new SqlCompletionData(t.Name, "table", 3));
        }
        else
        {
            foreach (var k in Keywords.Where(k => k.StartsWith(word, StringComparison.OrdinalIgnoreCase)).Take(60))
                data.Add(new SqlCompletionData(k, "keyword", 0));
            foreach (var f in Functions.Where(f => f.StartsWith(word, StringComparison.OrdinalIgnoreCase)).Take(40))
                data.Add(new SqlCompletionData(f, "function", 1));
            foreach (var t in Types.Where(t => t.StartsWith(word, StringComparison.OrdinalIgnoreCase)).Take(30))
                data.Add(new SqlCompletionData(t, "type", 1));
            foreach (var tbl in Tables.Take(300))
            {
                if (tbl.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase) ||
                    tbl.ShortName.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                    data.Add(new SqlCompletionData(tbl.ShortName, "table", 3));
            }
            if (data.Count == 0)
                return; // nothing relevant — don't pop up
            window.CompletionList.SelectItem(word);
        }

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

    private static IEnumerable<string> ResolveColumns(string token)
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
        return [];
    }

    private sealed class SqlCompletionData(string text, string kind, double priority) : ICompletionData
    {
        public Avalonia.Media.IImage? Image => null;
        public string Text { get; } = text;
        public object Content => Text;
        public object Description => kind;
        public double Priority { get; } = priority;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document?.Replace(completionSegment, Text);
        }
    }
}
