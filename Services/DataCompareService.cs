using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Row-level, key-based comparison of one table across two connections.
///
/// The target's rows are keyed in memory (bounded by the row limit) and the source is
/// streamed against them, so nothing depends on a sort order agreeing between servers,
/// collations or data types. Both connections are opened for reading only: the product
/// is a verdict plus a script, never a write.
/// </summary>
public sealed class DataCompareService
{
    /// <summary>Rows considered per side per table before the result is called partial.</summary>
    public const int DefaultRowLimit = 20_000;

    /// <summary>Differing rows listed in the UI per table.</summary>
    public const int MaxRowsShown = 200;

    /// <summary>Statements kept per table, so one pathological table cannot eat the script.</summary>
    public const int MaxStatements = 5_000;

    // ─── Discovery ───────────────────────────────────────────────────────────

    /// <summary>
    /// The tables both sides expose, each with the shared key and shared columns a
    /// comparison would use, or the reason it cannot. No table data is read.
    /// </summary>
    public async Task<(List<DataCompareTable> Tables, string? Warning)> DiscoverAsync(
        ConnectionInfo source, ConnectionInfo target, CancellationToken ct = default)
    {
        var sourceShapes = await ReadShapesAsync(source.ConnectionString, ct);
        var targetShapes = await ReadShapesAsync(target.ConnectionString, ct);
        var sourceCollation = await GetCollationAsync(source.ConnectionString, ct);
        var targetCollation = await GetCollationAsync(target.ConnectionString, ct);

        var names = new SortedSet<string>(
            sourceShapes.Keys.Concat(targetShapes.Keys), StringComparer.OrdinalIgnoreCase);
        var tables = new List<DataCompareTable>();

        foreach (var name in names)
        {
            sourceShapes.TryGetValue(name, out var s);
            targetShapes.TryGetValue(name, out var t);
            var dot = name.IndexOf('.');
            var (schema, table) = (name[..dot], name[(dot + 1)..]);

            if (s is null)
            {
                tables.Add(Skipped(schema, table, "Exists only on the target — the source has no such table."));
                continue;
            }
            if (t is null)
            {
                tables.Add(Skipped(schema, table, "Missing on the target — create it with schema compare before comparing data."));
                continue;
            }
            if (s.Keys.Count == 0 || t.Keys.Count == 0)
            {
                tables.Add(Skipped(schema, table, "A primary key is required on both sides to pair rows."));
                continue;
            }
            if (!s.Keys.SequenceEqual(t.Keys, StringComparer.OrdinalIgnoreCase))
            {
                tables.Add(Skipped(schema, table,
                    $"Primary key differs: {string.Join(", ", s.Keys)} here against {string.Join(", ", t.Keys)} there."));
                continue;
            }

            // A key column typed differently on the two sides would not pair reliably:
            // an int 5 and a decimal 5.00 are the same row to SQL Server, not to a key.
            var mismatched = s.Keys.FirstOrDefault(c =>
                !string.Equals(s.Types[c], t.Types[c], StringComparison.OrdinalIgnoreCase));
            if (mismatched != null)
            {
                tables.Add(Skipped(schema, table,
                    $"Key column {mismatched} is {s.Types[mismatched]} on the source but {t.Types[mismatched]} on the target."));
                continue;
            }

            var shared = s.Columns
                .Where(c => t.Columns.Contains(c, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var nonKey = shared
                .Where(c => !s.Keys.Contains(c, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (nonKey.Count == 0)
            {
                tables.Add(Skipped(schema, table, "The key is the only shared column — there is nothing else to compare."));
                continue;
            }

            tables.Add(new DataCompareTable
            {
                Schema = schema,
                Name = table,
                KeyColumns = s.Keys,
                CompareColumns = nonKey,
                PartialColumns = !SameColumnSet(s.Columns, t.Columns)
            });
        }

        string? warning = tables.Count switch
        {
            0 => "Neither database has a user table to compare.",
            _ when !string.Equals(sourceCollation, targetCollation, StringComparison.OrdinalIgnoreCase) =>
                $"The two databases use different collations ({sourceCollation} against {targetCollation}), so rows are " +
                "paired on exact key values rather than the server's sort rules: text keys that differ only in case or " +
                "accent will read as two different rows.",
            _ => null
        };
        return (tables, warning);
    }

    private static bool SameColumnSet(List<string> a, List<string> b) =>
        a.Count == b.Count && a.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(b.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    private static DataCompareTable Skipped(string schema, string name, string reason) =>
        new()
        {
            Schema = schema, Name = name, SkipReason = reason, IsSelected = false,
            State = TableDataState.Skipped
        };

    private sealed class TableShape
    {
        public List<string> Columns { get; } = [];
        public List<(int Ordinal, string Column)> KeyPairs { get; } = [];
        public Dictionary<string, string> Types { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Keys => KeyPairs.OrderBy(p => p.Ordinal).Select(p => p.Column).ToList();
    }

    private static async Task<Dictionary<string, TableShape>> ReadShapesAsync(string cs, CancellationToken ct)
    {
        const string query = """
            SELECT s.name, t.name, c.name, ty.name, c.is_computed, CAST(pk.key_ordinal AS int)
            FROM sys.tables AS t
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            JOIN sys.columns AS c ON c.object_id = t.object_id
            JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN (SELECT ic.object_id, ic.column_id, ic.key_ordinal
                       FROM sys.indexes AS i
                       JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                       WHERE i.is_primary_key = 1) AS pk
                   ON pk.object_id = c.object_id AND pk.column_id = c.column_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, c.column_id
            """;

        var map = new Dictionary<string, TableShape>(StringComparer.OrdinalIgnoreCase);
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var key = $"{rdr.GetString(0)}.{rdr.GetString(1)}";
            if (!map.TryGetValue(key, out var shape)) map[key] = shape = new TableShape();
            var column = rdr.GetString(2);
            var type = rdr.GetString(3);
            shape.Types[column] = type;
            // Computed columns and row versions are derived on each side, so they can be
            // neither compared meaningfully nor written.
            var derived = rdr.GetBoolean(4)
                || type.Equals("timestamp", StringComparison.OrdinalIgnoreCase)
                || type.Equals("rowversion", StringComparison.OrdinalIgnoreCase);
            if (!derived) shape.Columns.Add(column);
            if (!rdr.IsDBNull(5)) shape.KeyPairs.Add((rdr.GetInt32(5), column));
        }
        return map;
    }

    private static async Task<string> GetCollationAsync(string cs, CancellationToken ct)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT DATABASEPROPERTYEX(DB_NAME(), 'Collation')", conn)
        {
            CommandTimeout = 15
        };
        return await cmd.ExecuteScalarAsync(ct) is string s ? s : "";
    }

    // ─── Comparison ──────────────────────────────────────────────────────────

    /// <summary>
    /// Pairs the rows of one table on its key and reports what is missing, extra or
    /// changed, with the statements that would bring the target in line.
    /// </summary>
    public async Task<TableDataDiff> CompareAsync(
        ConnectionInfo source, ConnectionInfo target, DataCompareTable table,
        int rowLimit = DefaultRowLimit, bool includeDeletes = false, CancellationToken ct = default)
    {
        if (!table.IsComparable)
            throw new InvalidOperationException(
                $"{table.FullName} cannot be compared: {table.SkipReason ?? "it has no shared key."}");

        var keys = table.KeyColumns;
        var columns = keys.Concat(table.CompareColumns).ToList();
        var keyAt = keys.Select(k => columns.IndexOf(k)).ToList();
        var valueAt = table.CompareColumns.Select(c => columns.IndexOf(c)).ToList();
        var select = $"SELECT TOP ({rowLimit + 1}) {string.Join(", ", columns.Select(Q))} FROM {table.FullName}";
        var full = table.FullName;

        var targetRows = new Dictionary<string, object?[]>(StringComparer.Ordinal);
        long targetScanned = 0;
        bool truncated = false;

        await using (var conn = new SqlConnection(target.ConnectionString))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(select, conn) { CommandTimeout = 120 };
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                // TOP (rowLimit + 1) fetches one row too many on purpose: reading it is how
                // the scan knows it stopped early, and it is not a row it compared.
                if (targetScanned == rowLimit) { truncated = true; break; }
                targetScanned++;
                var row = ReadRow(rdr, columns.Count);
                targetRows[KeyOf(row, keyAt)] = row;
            }
        }

        var rows = new List<RowDiff>();
        var inserts = new List<string>();
        var updates = new List<string>();
        int missing = 0, changed = 0;
        long sourceScanned = 0;

        await using (var conn = new SqlConnection(source.ConnectionString))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(select, conn) { CommandTimeout = 120 };
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                if (sourceScanned == rowLimit) { truncated = true; break; }
                sourceScanned++;
                var row = ReadRow(rdr, columns.Count);
                var keyText = KeyOf(row, keyAt);
                if (!targetRows.TryGetValue(keyText, out var counterpart))
                {
                    missing++;
                    if (rows.Count < MaxRowsShown)
                        rows.Add(new RowDiff(RowDiffKind.MissingInTarget, DisplayKey(row, keyAt, keys),
                            "no row with this key on the target"));
                    if (inserts.Count < MaxStatements) inserts.Add(InsertStatement(full, columns, row));
                    continue;
                }

                targetRows.Remove(keyText); // matched: it cannot also be a target-only row
                var differing = new List<int>();
                for (var i = 0; i < valueAt.Count; i++)
                    if (!SameValue(row[valueAt[i]], counterpart[valueAt[i]])) differing.Add(i);
                if (differing.Count == 0) continue;

                changed++;
                if (rows.Count < MaxRowsShown)
                    rows.Add(new RowDiff(RowDiffKind.Changed, DisplayKey(row, keyAt, keys),
                        string.Join(", ", differing.Select(i => table.CompareColumns[i]))));
                if (updates.Count < MaxStatements)
                    updates.Add(UpdateStatement(full, keys, keyAt, columns, row, counterpart, differing, valueAt));
            }
        }

        var deletes = new List<string>();
        foreach (var row in targetRows.Values)
        {
            if (rows.Count < MaxRowsShown)
                rows.Add(new RowDiff(RowDiffKind.ExtraInTarget, DisplayKey(row, keyAt, keys), "exists only on the target"));
            if (includeDeletes && deletes.Count < MaxStatements)
                deletes.Add(DeleteStatement(full, keys, keyAt, row));
        }

        return new TableDataDiff
        {
            Table = table,
            SourceRows = sourceScanned,
            TargetRows = targetScanned,
            MissingInTarget = missing,
            ExtraInTarget = targetRows.Count,
            ChangedRows = changed,
            Truncated = truncated,
            Rows = rows,
            Inserts = inserts,
            Updates = updates,
            Deletes = deletes
        };
    }

    /// <summary>
    /// A plain value snapshot. The untyped <c>GetValue</c> is used rather than a typed
    /// accessor because one table can hold types the provider has no CLR mapping for,
    /// and an unreadable column must not abort the whole table.
    /// </summary>
    private static object?[] ReadRow(SqlDataReader rdr, int count)
    {
        var row = new object?[count];
        for (var i = 0; i < count; i++) row[i] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
        return row;
    }

    // ─── Script generation ───────────────────────────────────────────────────

    /// <summary>
    /// A review-first synchronization script. The caller saves or copies it; nothing in
    /// this class executes SQL against either database.
    /// </summary>
    public static string BuildSyncScript(
        ConnectionInfo source, ConnectionInfo target, IReadOnlyList<TableDataDiff> diffs,
        bool includeDeletes)
    {
        var header = new StringBuilder();
        header.AppendLine("-- Schema Compare — data synchronization script");
        header.AppendLine($"-- Source: {source.SafeForLog}");
        header.AppendLine($"-- Target: {target.SafeForLog}");
        header.AppendLine("-- Generated from a key-based row comparison. Nothing has been executed.");
        header.AppendLine(includeDeletes
            ? "-- Target-only rows are deleted. Back up the target first."
            : "-- Target-only rows are reported but NOT deleted.");
        header.AppendLine();

        var body = new StringBuilder();
        var statements = 0;
        var different = 0;
        foreach (var diff in diffs)
        {
            var table = diff.Table;
            if (diff.MissingInTarget == 0 && diff.ChangedRows == 0 && diff.ExtraInTarget == 0)
            {
                header.AppendLine($"-- {table.FullName}: identical");
                continue;
            }
            different++;
            header.AppendLine($"-- {table.FullName}: {diff.Summary}");
            foreach (var insert in diff.Inserts) body.AppendLine(insert);
            foreach (var update in diff.Updates) body.AppendLine(update);
            foreach (var delete in diff.Deletes) body.AppendLine(delete);
            statements += diff.Inserts.Count + diff.Updates.Count + diff.Deletes.Count;
            if (diff.Truncated)
                body.AppendLine($"-- {table.FullName}: the scan stopped at the row limit, so this table needs another pass.");
            if (diff.ExtraInTarget > 0 && !includeDeletes)
                body.AppendLine($"-- {table.FullName}: {diff.ExtraInTarget} target-only row(s) left in place.");
            body.AppendLine();
        }

        if (statements == 0)
            return header.Append("-- No row-level differences to write.").AppendLine().ToString();

        header.AppendLine("SET XACT_ABORT ON;");
        header.AppendLine("BEGIN TRANSACTION;");
        header.AppendLine();
        return header.Append(body).ToString() +
               $"COMMIT TRANSACTION;\n-- {statements} statement(s) across {different} table(s).\n";
    }

    public static string InsertStatement(string fullTable, IReadOnlyList<string> columns, IReadOnlyList<object?> row)
    {
        var names = string.Join(", ", columns.Select(Q));
        var values = string.Join(", ", row.Select(Literal));
        return $"INSERT INTO {fullTable} ({names}) VALUES ({values});";
    }

    /// <summary>
    /// Only the columns that actually differ are written, and the values the target
    /// holds today go into the comment above the statement — a reviewer needs them to
    /// judge the change, and they are the only way back.
    /// </summary>
    public static string UpdateStatement(
        string fullTable, IReadOnlyList<string> keys, IReadOnlyList<int> keyAt, IReadOnlyList<string> columns,
        IReadOnlyList<object?> sourceRow, IReadOnlyList<object?> targetRow,
        IReadOnlyList<int> differing, IReadOnlyList<int> valueAt)
    {
        var was = string.Join(", ", differing.Select(i =>
            $"{columns[valueAt[i]]} was {Literal(targetRow[valueAt[i]])}"));
        var set = string.Join(", ", differing.Select(i =>
            $"{Q(columns[valueAt[i]])} = {Literal(sourceRow[valueAt[i]])}"));
        return $"-- {KeyText(keys, keyAt, sourceRow)}: {was}\n" +
               $"UPDATE {fullTable} SET {set} WHERE {KeyPredicate(keys, keyAt, sourceRow)};";
    }

    public static string DeleteStatement(
        string fullTable, IReadOnlyList<string> keys, IReadOnlyList<int> keyAt, IReadOnlyList<object?> row) =>
        $"DELETE FROM {fullTable} WHERE {KeyPredicate(keys, keyAt, row)};";

    private static string KeyPredicate(
        IReadOnlyList<string> keys, IReadOnlyList<int> keyAt, IReadOnlyList<object?> row) =>
        string.Join(" AND ", keys.Select((k, i) => row[keyAt[i]] is null
            ? $"{Q(k)} IS NULL"
            : $"{Q(k)} = {Literal(row[keyAt[i]])}"));

    private static string KeyText(
        IReadOnlyList<string> keys, IReadOnlyList<int> keyAt, IReadOnlyList<object?> row) =>
        string.Join(", ", keys.Select((k, i) => $"{k} = {Literal(row[keyAt[i]])}"));

    private static string DisplayKey(
        IReadOnlyList<object?> row, IReadOnlyList<int> keyAt, IReadOnlyList<string> keys) =>
        string.Join(" · ", keys.Select((k, i) => $"{k}={ShortValue(row[keyAt[i]])}"));

    private static string ShortValue(object? value)
    {
        var text = value switch
        {
            null => "NULL",
            byte[] b => "0x" + Convert.ToHexString(b.AsSpan(0, Math.Min(4, b.Length))),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? ""
        };
        return text.Length > 32 ? text[..32] + "…" : text;
    }

    /// <summary>A T-SQL literal for a CLR value that came back from SQL Server.</summary>
    public static string Literal(object? value) => value switch
    {
        null or DBNull => "NULL",
        bool b => b ? "CAST(1 AS bit)" : "CAST(0 AS bit)",
        string s => "N'" + s.Replace("'", "''", StringComparison.Ordinal) + "'",
        char c => "N'" + c + "'",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        Guid g => $"'{g:D}'",
        DateTime d => $"'{d:yyyy-MM-dd HH:mm:ss.fffffff}'",
        DateTimeOffset o => $"'{o:yyyy-MM-dd HH:mm:ss.fffffff zzz}'",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        byte or sbyte or short or ushort or int or long =>
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
        TimeSpan t => $"'{t}'",
        IFormattable f =>
            "N'" + f.ToString(null, CultureInfo.InvariantCulture)?.Replace("'", "''", StringComparison.Ordinal) + "'",
        _ => "N'" + (value.ToString() ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'"
    };

    /// <summary>
    /// True when both sides hold the same value. Numbers compare by value across types
    /// (a 1 against a 1.00 is not a change) and text compares exactly, so a real
    /// difference in what is stored is never called equal.
    /// </summary>
    public static bool SameValue(object? a, object? b)
    {
        var an = a is null or DBNull;
        var bn = b is null or DBNull;
        if (an || bn) return an && bn;
        if (ReferenceEquals(a, b)) return true;

        switch (a, b)
        {
            case (byte[] x, byte[] y): return x.AsSpan().SequenceEqual(y);
            case (string x, string y): return string.Equals(x, y, StringComparison.Ordinal);
            case (bool x, bool y): return x == y;
            case (DateTime x, DateTime y): return x == y;
            case (DateTimeOffset x, DateTimeOffset y): return x == y;
            case (TimeSpan x, TimeSpan y): return x == y;
            case (Guid x, Guid y): return x == y;
        }

        var bothExact = a is byte or sbyte or short or ushort or int or long or decimal
                        && b is byte or sbyte or short or ushort or int or long or decimal;
        if (bothExact || (IsNumber(a) && IsNumber(b)))
        {
            try
            {
                return bothExact
                    ? Convert.ToDecimal(a, CultureInfo.InvariantCulture) ==
                        Convert.ToDecimal(b, CultureInfo.InvariantCulture)
                    : Convert.ToDouble(a, CultureInfo.InvariantCulture) ==
                        Convert.ToDouble(b, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                // The text comparison below still answers it.
            }
        }

        // A number against a string is a real difference however both print, and the
        // key builder already refuses to pair them — the comparison must agree.
        if (IsNumber(a) != IsNumber(b)) return false;

        return string.Equals(TextForm(a), TextForm(b), StringComparison.Ordinal);
    }

    private static bool IsNumber(object? v) =>
        v is byte or sbyte or short or ushort or int or long or decimal or float or double;

    private static string TextForm(object? v) =>
        v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) ?? "" : v?.ToString() ?? "";

    /// <summary>
    /// Canonical text for a key: rows pair when their keys match exactly, since a server
    /// sort rule that differs between instances would otherwise pair unrelated rows or
    /// split matching ones. The literal keeps the type in the text, so the number 1 and
    /// the text '1' cannot collide.
    /// </summary>
    private static string KeyOf(IReadOnlyList<object?> row, IReadOnlyList<int> keyAt)
    {
        var parts = new string[keyAt.Count];
        for (var i = 0; i < keyAt.Count; i++) parts[i] = Literal(row[keyAt[i]]);
        return string.Join(UnitSeparator, parts);
    }

    private const char UnitSeparator = (char)31;

    private static string Q(string name) =>
        "[" + name.Trim().TrimStart('[').TrimEnd(']').Replace("]", "]]", StringComparison.Ordinal) + "]";
}

