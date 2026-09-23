using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Executes ad-hoc SQL batches for the Query window.
/// Multi-statement scripts are split on GO separators; every SELECT result set
/// and every action-statement row count is returned in execution order.
/// Row counts are capped per result set to protect the UI.
/// </summary>
public sealed class QueryExecutionService
{
    public const int MaxRowsPerResult = 5_000;
    /// <summary>Per-cell text cap so LOB columns cannot exhaust memory (SSMS-style).</summary>
    public const int MaxCellChars = 65_536;
    /// <summary>Per-cell binary cap.</summary>
    public const int MaxBinaryCellBytes = 1_048_576;
    /// <summary>Approximate text budget per result set (~100 MB of UTF-16).</summary>
    public const long MaxCharsPerResult = 50_000_000;
    public const int DefaultCommandTimeoutSeconds = 120;

    /// <summary>Runs the batch and streams result tables in order.</summary>
    /// <param name="onStatus">
    /// Optional progress callback ("Connecting...", "Sending batch 1/2...",
    /// "Receiving rows..."). Invoked on the caller's synchronization context.
    /// </param>
    public async Task<IReadOnlyList<QueryResultTable>> ExecuteAsync(
        ConnectionInfo info,
        string sql,
        int timeoutSeconds = DefaultCommandTimeoutSeconds,
        CancellationToken ct = default,
        Action<string>? onStatus = null)
    {
        var batches = SplitBatches(sql);
        var results = new List<QueryResultTable>();
        var index = 0;

        onStatus?.Invoke($"Connecting to {info.Server}/{info.Database}...");
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        var target = string.IsNullOrWhiteSpace(conn.DataSource) ? info.Server : conn.DataSource;

        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            onStatus?.Invoke($"Sending batch {index}/{batches.Count} to {target}/{info.Database}...");
            await ExecuteSingleBatchAsync(conn, batch, index, batches.Count, results, timeoutSeconds, target, ct, onStatus);
        }

        return results;
    }

    private static async Task ExecuteSingleBatchAsync(
        SqlConnection conn,
        string batch,
        int batchNumber,
        int batchCount,
        List<QueryResultTable> results,
        int timeoutSeconds,
        string target,
        CancellationToken ct,
        Action<string>? onStatus)
    {
        var label = batchCount > 1 ? $"Batch {batchNumber}" : "Result";
        await using var cmd = new SqlCommand(batch, conn) { CommandTimeout = timeoutSeconds };
        var started = DateTime.UtcNow;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        onStatus?.Invoke($"Batch {batchNumber}/{batchCount} sent to {target} - receiving rows...");
        var setNumber = 0;
        do
        {
            // Action statements yield no columns — report their row count.
            if (reader.FieldCount == 0)
            {
                var affected = reader.RecordsAffected;
                if (affected >= 0 || setNumber == 0)
                {
                    results.Add(new QueryResultTable
                    {
                        Title = affected >= 0 ? $"{label} — {affected:N0} row(s) affected" : label,
                        AffectedRows = affected >= 0 ? affected : 0,
                        Elapsed = DateTime.UtcNow - started
                    });
                }
                continue;
            }

            setNumber++;
            var columns = ReadColumnNames(reader);
            var (rows, truncated) = await ReadRowsAsync(reader, columns, ct);

            results.Add(new QueryResultTable
            {
                Title = batchCount > 1 ? $"{label} — Result {setNumber}" : $"Result {results.Count + 1}",
                Columns = columns,
                Rows = rows,
                IsTruncated = truncated,
                Elapsed = DateTime.UtcNow - started
            });

            if (truncated)
            {
                // Stop the server streaming the rest instead of draining it
                // row-by-row over the wire (draining can take minutes on big tables).
                try { cmd.Cancel(); } catch { /* already finished */ }
                try
                {
                    // Consume the attention packet so the connection stays usable
                    // for the next batch; the server stops sending immediately.
                    while (await reader.ReadAsync(CancellationToken.None)) { }
                }
                catch (SqlException) { /* "operation canceled by user" - expected */ }
                catch (InvalidOperationException) { /* reader already closed */ }
                return; // remaining result sets belong to the aborted stream
            }
        }
        while (await reader.NextResultAsync(ct));
    }

    private static List<string> ReadColumnNames(SqlDataReader reader)
    {
        var columns = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (string.IsNullOrWhiteSpace(name))
                name = $"Column{i + 1}";
            // Disambiguate duplicate column names so dictionary keys stay unique.
            var unique = name;
            var suffix = 2;
            while (columns.Contains(unique, StringComparer.OrdinalIgnoreCase))
                unique = $"{name}_{suffix++}";
            columns.Add(unique);
        }
        return columns;
    }

    private static async Task<(List<Dictionary<string, object?>> Rows, bool Truncated)> ReadRowsAsync(
        SqlDataReader reader, List<string> columns, CancellationToken ct)
    {
        var rows = new List<Dictionary<string, object?>>();
        long chars = 0;
        while (await reader.ReadAsync(ct))
        {
            // Cap first: the caller cancels the command instead of draining the
            // rest of the result over the wire (draining can take minutes).
            if (rows.Count >= MaxRowsPerResult || chars >= MaxCharsPerResult)
                return (rows, true);

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                if (value is string s)
                {
                    chars += s.Length;
                    if (s.Length > MaxCellChars)
                        value = s[..MaxCellChars] + " ... (truncated)";
                }
                else if (value is byte[] b)
                {
                    chars += b.Length;
                    if (b.Length > MaxBinaryCellBytes)
                    {
                        var cut = new byte[MaxBinaryCellBytes];
                        Buffer.BlockCopy(b, 0, cut, 0, cut.Length);
                        value = cut;
                    }
                }
                row[columns[i]] = value;
            }
            rows.Add(row);
        }
        return (rows, false);
    }

    /// <summary>
    /// Splits a script on GO batch separators: a line holding only GO plus an
    /// optional repeat count (trailing -- comment allowed). String literals and
    /// block comments are respected so GO inside them is not treated as a separator.
    /// </summary>
    internal static List<string> SplitBatches(string sql)
    {
        var batches = new List<string>();
        var current = new StringBuilder();
        var inSingleQuote = false;
        var inBlockComment = false;
        var lineStart = 0;
        var i = 0;

        void EndLine(bool keepBreak)
        {
            var line = sql.Substring(lineStart, i - lineStart);
            if (!inSingleQuote && !inBlockComment && IsGoSeparator(line))
            {
                var batch = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(batch))
                    batches.Add(batch);
                current.Clear();
            }
            else
            {
                current.Append(line);
                if (keepBreak)
                    current.Append('\n');
            }
            lineStart = i + 1;
        }

        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (c == '\n')
            {
                EndLine(keepBreak: false);
            }
            else if (c == '\r')
            {
                EndLine(keepBreak: next != '\n');
                if (next == '\n') i++; // CRLF counts as one line break
            }
            else if (inBlockComment)
            {
                if (c == '*' && next == '/') { inBlockComment = false; i++; }
            }
            else if (inSingleQuote)
            {
                if (c == '\'' && next == '\'')
                    i++; // escaped quote ''
                else if (c == '\'')
                    inSingleQuote = false;
            }
            else if (c == '/' && next == '*')
            {
                inBlockComment = true; i++;
            }
            else if (c == '\'')
            {
                inSingleQuote = true;
            }
            i++;
        }

        if (lineStart <= sql.Length)
        {
            i = sql.Length;
            EndLine(keepBreak: false);
        }

        var tail = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            batches.Add(tail);

        return batches.Count == 0 ? [""] : batches;
    }

    private static bool IsGoSeparator(string line)
    {
        var commentAt = line.IndexOf("--", StringComparison.Ordinal);
        var code = (commentAt >= 0 ? line[..commentAt] : line).Trim();
        if (code.Length < 2)
            return false;
        if (!code.StartsWith("GO", StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = code[2..].Trim();
        if (rest.Length == 0)
            return true;
        return int.TryParse(rest, out _);
    }
}
