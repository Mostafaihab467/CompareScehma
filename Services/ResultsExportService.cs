using System.Globalization;
using System.Text;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Renders a <see cref="QueryResultTable"/> as TSV or CSV text — SSMS
/// "Copy with Headers" / "Save Results As" equivalents. Pure and testable.
/// </summary>
public static class ResultsExportService
{
    public const int DefaultMaxRows = 5_000;

    public static string ToTsv(QueryResultTable table, int maxRows = DefaultMaxRows) =>
        Join(table, "\t", quote: null, maxRows);

    public static string ToCsv(QueryResultTable table, int maxRows = DefaultMaxRows) =>
        Join(table, ",", quote: "\"", maxRows);

    private static string Join(QueryResultTable table, string sep, string? quote, int maxRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(sep, table.Columns.Select(c => Escape(c, quote))));
        var count = Math.Min(table.Rows.Count, maxRows);
        for (var i = 0; i < count; i++)
        {
            var row = table.Rows[i];
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
