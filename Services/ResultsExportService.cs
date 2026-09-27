using System.Globalization;
using System.Text;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Renders a <see cref="QueryResultTable"/> as TSV, CSV, JSON, Markdown or
/// INSERT scripts — SSMS "Copy with Headers" / "Save Results As" equivalents.
/// Pure and testable.
/// </summary>
public static class ResultsExportService
{
    public const int DefaultMaxRows = 5_000;

    public static string ToTsv(QueryResultTable table, int maxRows = DefaultMaxRows) =>
        Join(table, "\t", quote: null, maxRows);

    public static string ToCsv(QueryResultTable table, int maxRows = DefaultMaxRows) =>
        Join(table, ",", quote: "\"", maxRows);

    /// <summary>Array of objects, one per row, keyed by column name.</summary>
    public static string ToJson(QueryResultTable table, int maxRows = DefaultMaxRows)
    {
        // Relaxed escaping only for JSON that is read as data (a file, a paste):
        // the default encoder turns ' into \u0027, which is valid but unreadable.
        var options = new System.Text.Json.JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream, options))
        {
            writer.WriteStartArray();
            foreach (var row in Rows(table, maxRows))
            {
                writer.WriteStartObject();
                foreach (var column in table.Columns)
                    WriteJson(writer, column, row.TryGetValue(column, out var v) ? v : null);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteJson(System.Text.Json.Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null or DBNull: writer.WriteNull(name); break;
            case string s: writer.WriteString(name, s); break;
            case bool b: writer.WriteBoolean(name, b); break;
            case byte[] bytes: writer.WriteString(name, Convert.ToBase64String(bytes)); break;
            case DateTime dt: writer.WriteString(name, dt.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset dto: writer.WriteString(name, dto.ToString("o", CultureInfo.InvariantCulture)); break;
            // Numbers stay numbers: a quoted "1" breaks every consumer that reads the file.
            case int i: writer.WriteNumber(name, i); break;
            case long l: writer.WriteNumber(name, l); break;
            case short sh: writer.WriteNumber(name, sh); break;
            case byte bt: writer.WriteNumber(name, bt); break;
            case sbyte sbv: writer.WriteNumber(name, sbv); break;
            case ushort us: writer.WriteNumber(name, us); break;
            case uint ui: writer.WriteNumber(name, ui); break;
            case ulong ul: writer.WriteNumber(name, ul); break;
            case decimal m: writer.WriteNumber(name, m); break;
            // float 10/53 and real can honestly be NaN or +/-inf, which JSON cannot hold.
            case double d when double.IsNaN(d) || double.IsInfinity(d):
                writer.WriteString(name, d.ToString("R", CultureInfo.InvariantCulture)); break;
            case float f when float.IsNaN(f) || float.IsInfinity(f):
                writer.WriteString(name, f.ToString("R", CultureInfo.InvariantCulture)); break;
            case double d: writer.WriteNumber(name, d); break;
            case float f: writer.WriteNumber(name, f); break;
            case IFormattable formattable:
                writer.WriteString(name, formattable.ToString(null, CultureInfo.InvariantCulture)); break;
            default: writer.WriteString(name, value.ToString()); break;
        }
    }

    /// <summary>GitHub-flavoured table. Pipe and newline are escaped so cell
    /// content cannot forge a row.</summary>
    public static string ToMarkdown(QueryResultTable table, int maxRows = DefaultMaxRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| " + string.Join(" | ", table.Columns.Select(MdCell)) + " |");
        sb.AppendLine("|" + string.Join("|", table.Columns.Select(_ => " --- ")) + "|");
        foreach (var row in Rows(table, maxRows))
            sb.AppendLine("| " + string.Join(" | ",
                table.Columns.Select(c => MdCell(Format(row.TryGetValue(c, out var v) ? v : null)))) + " |");
        return sb.ToString();
    }

    private static string MdCell(string raw) =>
        raw.Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");

    /// <summary>
    /// INSERT statements for the result set, for moving rows between servers.
    /// <paramref name="targetTable"/> is schema-qualified by the caller; a bare
    /// name is qualified as <c>[dbo].[name]</c>.
    /// </summary>
    public static string ToInsertScripts(QueryResultTable table, string targetTable, int maxRows = DefaultMaxRows)
    {
        var sb = new StringBuilder();
        var target = Qualified(targetTable);
        var columns = string.Join(", ", table.Columns.Select(Bracket));
        foreach (var row in Rows(table, maxRows))
        {
            var values = string.Join(", ", table.Columns.Select(c =>
                SqlLiteral(row.TryGetValue(c, out var v) ? v : null)));
            sb.AppendLine($"INSERT INTO {target} ({columns}) VALUES ({values});");
        }
        return sb.ToString();
    }

    /// <summary>The table a SELECT reads from, used as the default INSERT target.
    /// Null when the script has no readable FROM clause or names more than one
    /// table, so the caller can ask instead of guessing.</summary>
    public static string? InsertTargetFrom(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return null;
        // A join, a comma list, a subquery or a CTE means the rows do not come
        // from one stored table, so there is nothing honest to name as a target.
        if (System.Text.RegularExpressions.Regex.IsMatch(sql, @"(?i)\b(join|with)\b")) return null;
        var froms = System.Text.RegularExpressions.Regex.Matches(sql, @"(?i)\bfrom\b");
        if (froms.Count != 1) return null;
        var after = sql[(froms[0].Index + froms[0].Length)..];
        var m = System.Text.RegularExpressions.Regex.Match(after, @"^\s+(\[[^\]]+\]|[\w@#]+)(\s*\.\s*(\[[^\]]+\]|[\w@#]+))?");
        if (!m.Success) return null;
        if (after[m.Length..].TrimStart().StartsWith(",")) return null;
        return Dot(m.Value.Trim());
    }

    private static string Dot(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"\s*\.\s*", ".");

    private static IEnumerable<Dictionary<string, object?>> Rows(QueryResultTable table, int maxRows)
    {
        // The rows on screen, not the rows the server sent: an export that quietly ignored the
        // filters the operator is looking at hands them 5 000 rows they never asked to see.
        var count = Math.Min(table.VisibleRows.Count, maxRows);
        for (var i = 0; i < count; i++) yield return table.VisibleRows[i];
    }

    private static string Qualified(string name)
    {
        var parts = name.Replace("[", "").Replace("]", "").Split('.', StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => $"[dbo].[{parts[0]}]",
            2 => $"[{parts[0]}].[{parts[1]}]",
            _ => $"[{parts[^2]}].[{parts[^1]}]"
        };
    }

    private static string Bracket(string name) => "[" + name.Replace("]", "]]") + "]";

    /// <summary>Value as a T-SQL literal: N'' for text, 0x for binary, NULL for null.</summary>
    private static string SqlLiteral(object? value) => value switch
    {
        null or DBNull => "NULL",
        string s => "N'" + s.Replace("'", "''") + "'",
        bool b => b ? "1" : "0",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        DateTime dt => "'" + dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'",
        DateTimeOffset dto => "'" + dto.ToString("yyyy-MM-dd HH:mm:ss.fffzzz", CultureInfo.InvariantCulture) + "'",
        Guid g => "'" + g + "'",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => "N'" + (value.ToString() ?? "").Replace("'", "''") + "'"
    };

    private static string Join(QueryResultTable table, string sep, string? quote, int maxRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(sep, table.Columns.Select(c => Escape(c, quote))));
        var count = Math.Min(table.VisibleRows.Count, maxRows);
        for (var i = 0; i < count; i++)
        {
            var row = table.VisibleRows[i];
            sb.AppendLine(string.Join(sep, table.Columns.Select(c =>
                Escape(Format(row.TryGetValue(c, out var v) ? v : null), quote))));
        }
        return sb.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => "NULL",
        DBNull => "NULL",
        string s => s,
        bool b => b ? "1" : "0",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        byte[] bytes => "0x" + (bytes.Length <= 256
            ? Convert.ToHexString(bytes)
            : Convert.ToHexString(bytes, 0, 256) + "…"),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static string Escape(string raw, string? quote)
    {
        if (quote == null)
            return raw.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        return raw.Contains(quote) || raw.Contains(',') || raw.Contains('\n') || raw.Contains('\r')
            ? quote + raw.Replace(quote, quote + quote) + quote
            : raw;
    }
}
