using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Reads a delimited text file, guesses what each column holds, and loads the rows into
/// SQL Server with <see cref="SqlBulkCopy" />. Type guessing and script building are
/// static and pure so they can be tested without a server; the load itself is the only
/// part that writes, and it runs inside one transaction that rolls back whole.
/// </summary>
public sealed class CsvImportService
{
    /// <summary>Records read from the file to decide types — enough to be confident, few enough to be instant.</summary>
    public const int SampleRows = 500;

    /// <summary>Rows buffered before a bulk write; also how much a rollback can cost.</summary>
    public const int BatchRows = 5_000;

    /// <summary>Delimiters the wizard offers, with the characters they mean.</summary>
    public static readonly (string Label, char Char)[] Delimiters =
    [
        ("Comma ,", ','), ("Tab", '\t'), ("Semicolon ;", ';'), ("Pipe |", '|')
    ];

    /// <summary>Types the mapping dropdown offers, longest text first so it never truncates.</summary>
    public static readonly string[] SqlTypes =
    [
        "nvarchar(max)", "nvarchar(4000)", "nvarchar(500)", "nvarchar(200)", "nvarchar(100)", "nvarchar(50)",
        "varchar(50)", "char(10)", "int", "bigint", "smallint", "tinyint", "bit",
        "decimal(18,2)", "decimal(38,10)", "float", "real", "money",
        "date", "datetime2(3)", "datetime", "datetimeoffset(3)", "time",
        "uniqueidentifier", "varbinary(max)"
    ];

    /// <summary>
    /// Read the header and the first <paramref name="sampleRows"/> records. The file is
    /// opened shared-read so a spreadsheet that still has it open does not block the import.
    /// </summary>
    public static CsvTable ReadTable(string path, char delimiter, bool hasHeader, int sampleRows = SampleRows)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The file is not there any more.", path);

        var headers = new List<string>();
        var sample = new List<string?[]>();
        foreach (var record in EnumerateRecords(path, delimiter))
        {
            if (headers.Count == 0)
            {
                if (hasHeader)
                {
                    headers.AddRange(record.Select((c, i) =>
                        string.IsNullOrWhiteSpace(c) ? $"Column {i + 1}" : c.Trim()));
                    continue;
                }
                headers.AddRange(Enumerable.Range(0, record.Length).Select(i => $"Column {i + 1}"));
            }

            if (sample.Count >= sampleRows) break;
            sample.Add(Pad(record, headers.Count));
        }

        if (headers.Count == 0) throw new InvalidDataException("The file has no rows to read.");
        return new CsvTable { Headers = headers, Sample = sample };
    }

    /// <summary>
    /// Yield every record in the file as its fields, RFC 4180 style: a quoted field may
    /// hold the delimiter, a doubled quote or a line break. An empty field comes back as
    /// null, which is what a NULL column wants.
    /// </summary>
    public static IEnumerable<string?[]> EnumerateRecords(string path, char delimiter)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var fields = new List<string?>();
        var field = new StringBuilder();
        var recordHasContent = false;
        var inQuotes = false;

        int c;
        while ((c = reader.Read()) != -1)
        {
            var ch = (char)c;
            if (inQuotes)
            {
                if (ch != '"') { field.Append(ch); continue; }
                if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                else inQuotes = false;
                continue;
            }

            if (ch == '"') { inQuotes = true; recordHasContent = true; }
            else if (ch == delimiter) { fields.Add(End(field)); recordHasContent = true; }
            else if (ch == '\r') { /* the \n that follows closes the record */ }
            else if (ch == '\n')
            {
                fields.Add(End(field));
                if (recordHasContent || fields.Count > 1) yield return fields.ToArray();
                fields.Clear();
                recordHasContent = false;
            }
            else { field.Append(ch); recordHasContent = true; }
        }

        fields.Add(End(field));
        if (recordHasContent || fields.Count > 1) yield return fields.ToArray();
        static string? End(StringBuilder builder)
        {
            var text = builder.ToString();
            builder.Clear();
            return text.Length == 0 ? null : text;
        }
    }

    /// <summary>
    /// Pick a SQL type from the values a column actually holds. The answer is the widest
    /// type the sample still fits, so a file that looks like integers but carries one
    /// decimal further down fails loudly instead of rounding quietly.
    /// </summary>
    public static (string Type, bool Nullable, string Note) InferType(IReadOnlyList<string?> values)
    {
        var present = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList();
        var nullable = present.Count < values.Count;
        if (present.Count == 0) return ("nvarchar(50)", true, "no values in the sample");

        if (present.All(v => BoolValues.Contains(v.ToLowerInvariant())))
            return ("bit", nullable, $"values like {present[0]}");

        if (present.All(v => Guid.TryParse(v, out _)))
            return ("uniqueidentifier", nullable, "all values are GUIDs");

        if (present.All(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            var numbers = present.Select(v => long.Parse(v, CultureInfo.InvariantCulture)).ToList();
            var type = numbers.Max() <= int.MaxValue && numbers.Min() >= int.MinValue ? "int" : "bigint";
            return (type, nullable, $"{numbers.Min()} … {numbers.Max()}");
        }

        if (present.All(v => decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            var scale = present.Max(v => v.Contains('.') ? v.Split('.')[^1].Length : 0);
            var digits = present.Max(v => v.TrimStart('-', '+').Replace(".", "").TrimStart('0').Length);
            var precision = Math.Min(38, Math.Max(18, digits + scale));
            return ($"decimal({precision},{scale})", nullable,
                scale == 0 ? "whole numbers too large for int" : $"{scale} decimal place(s)");
        }

        if (present.All(v => DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
        {
            var hasTime = present.Any(v => v.Length > 11 && v.Contains(':'));
            return (hasTime ? "datetime2(3)" : "date", nullable, $"dates like {present[0]}");
        }

        var longest = present.Max(v => v.Length);
        if (longest > 4000) return ("nvarchar(max)", nullable, $"up to {longest} characters");
        return ($"nvarchar({Math.Min(4000, Math.Max(50, (longest / 10 + 1) * 10))})", nullable,
            $"up to {longest} characters");
    }

    private static readonly string[] BoolValues = ["true", "false", "1", "0", "yes", "no", "y", "n"];

    /// <summary>
    /// A usable SQL identifier: letters, digits and underscores, never a leading digit.
    /// Leading underscores are kept — they mean something on SQL Server (#temp, ##global,
    /// and the __ names probe tables use) and dropping them would rename the object.
    /// </summary>
    public static string SanitizeName(string raw)
    {
        var builder = new StringBuilder(raw.Trim());
        for (var i = 0; i < builder.Length; i++)
            if (!char.IsLetterOrDigit(builder[i]) && builder[i] != '_' && builder[i] != '#') builder[i] = '_';
        var text = builder.ToString().TrimEnd('_');
        if (text.Length == 0) return "Column";
        if (char.IsDigit(text[0])) text = "_" + text;
        return text.Length > 120 ? text[..120] : text;
    }

    /// <summary>
    /// The CREATE TABLE the wizard previews and runs. A table with no key columns is still
    /// a legal table — the wizard does not invent a key it was not given.
    /// </summary>
    public static string BuildCreateTable(string schema, string table, IReadOnlyList<ImportColumn> columns,
        IReadOnlyList<string>? keyColumns = null)
    {
        var usable = columns.Where(c => c.Include && !string.IsNullOrWhiteSpace(c.TargetName)).ToList();
        if (usable.Count == 0) throw new InvalidOperationException("Map at least one column.");
        var lines = usable.Select(c =>
            $"    [{c.TargetName.Trim()}] {c.SqlType.Trim()}{(c.Nullable ? " NULL" : " NOT NULL")}").ToList();
        var keys = (keyColumns ?? [])
            .Where(k => usable.Any(c => c.TargetName.Trim().Equals(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (keys.Count > 0)
            lines.Add($"    CONSTRAINT [PK_{SanitizeName(table)}] PRIMARY KEY ({string.Join(", ", keys.Select(k => $"[{k}]"))})");

        return $"IF OBJECT_ID(N'{schema}.{table}', N'U') IS NULL\n" +
               $"    CREATE TABLE [{schema}].[{table}]\n    (\n" +
               string.Join(",\n", lines) + "\n    );";
    }

    /// <summary>Existence check that does not care about the schema's case rules.</summary>
    public static async Task<bool> TableExistsAsync(string connectionString, string schema, string table,
        CancellationToken ct = default)
    {
        const string sql = "SELECT OBJECT_ID(@name, 'U');";
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 20 };
        cmd.Parameters.AddWithValue("@name", $"{schema}.{table}");
        var found = await cmd.ExecuteScalarAsync(ct);
        return found != null && found != DBNull.Value;
    }

    /// <summary>Columns of an existing table, so a mapping can be checked before anything is written.</summary>
    public static async Task<List<TargetColumn>> ReadTargetColumnsAsync(string connectionString, string schema,
        string table, CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.name,
                   ty.name + CASE WHEN ty.name IN ('nvarchar','nchar') AND c.max_length < 0 THEN '(max)'
                                  WHEN ty.name IN ('nvarchar','nchar') THEN '(' + CAST(c.max_length / 2 AS varchar(10)) + ')'
                                  WHEN ty.name IN ('varchar','char') AND c.max_length < 0 THEN '(max)'
                                  WHEN ty.name IN ('varchar','char') THEN '(' + CAST(c.max_length AS varchar(10)) + ')'
                                  WHEN ty.name IN ('decimal','numeric') THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                                  ELSE '' END,
                   c.is_nullable, c.is_identity, c.is_computed
            FROM sys.columns AS c
            JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@name)
            ORDER BY c.column_id
            """;

        var columns = new List<TargetColumn>();
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 20 };
        cmd.Parameters.AddWithValue("@name", $"{schema}.{table}");
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
            columns.Add(new TargetColumn
            {
                Name = rdr.GetString(0), Type = rdr.GetString(1),
                Nullable = rdr.GetBoolean(2), IsIdentity = rdr.GetBoolean(3), IsComputed = rdr.GetBoolean(4)
            });
        return columns;
    }

    /// <summary>
    /// Create the table when asked and stream the file into it. Every row goes through one
    /// transaction: a value that does not fit its column aborts the load and leaves the
    /// destination exactly as it was rather than half-filled.
    /// </summary>
    public async Task<ImportResult> LoadAsync(ConnectionInfo target, string schema, string table,
        IReadOnlyList<ImportColumn> columns, string path, char delimiter, bool hasHeader,
        bool createTable, IReadOnlyList<string>? keyColumns = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var mapped = columns.Where(c => c.Include).ToList();
        if (mapped.Count == 0) throw new InvalidOperationException("Map at least one column.");
        if (mapped.Any(c => string.IsNullOrWhiteSpace(c.TargetName)))
            throw new InvalidOperationException("Every included column needs a target column name.");
        if (mapped.Select(c => c.TargetName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != mapped.Count)
            throw new InvalidOperationException("Two CSV columns map to the same target column.");

        // The DDL built here has to be the DDL the operator confirmed in the preview, so the
        // ticked key columns travel with the request instead of being dropped on the way in.
        var ddl = BuildCreateTable(schema, table, mapped, keyColumns);

        progress?.Report($"Reading {Path.GetFileName(path)}…");
        await using var conn = new SqlConnection(target.ConnectionString);
        await conn.OpenAsync(ct);

        var created = false;
        if (createTable)
        {
            await using (var cmd = new SqlCommand(ddl, conn) { CommandTimeout = 60 })
                await cmd.ExecuteNonQueryAsync(ct);
            created = true;
            progress?.Report($"Table {schema}.{table} is there.");
        }

        var actual = await ReadTargetColumnsAsync(target.ConnectionString, schema, table, ct);
        if (actual.Count == 0)
            throw new InvalidOperationException($"{schema}.{table} does not exist in {target.Database}.");
        var unknown = mapped.Where(c => !actual.Any(a =>
            a.Name.Equals(c.TargetName.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException("The table has no column(s): " +
                string.Join(", ", unknown.Select(c => c.TargetName)) + ".");
        var computed = mapped.Where(c => actual.Any(a =>
            a.IsComputed && a.Name.Equals(c.TargetName.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        if (computed.Count > 0)
            throw new InvalidOperationException("Computed column(s) cannot be loaded: " +
                string.Join(", ", computed.Select(c => c.TargetName)) + ".");
        var keepIdentity = mapped.Any(c => actual.Any(a =>
            a.IsIdentity && a.Name.Equals(c.TargetName.Trim(), StringComparison.OrdinalIgnoreCase)));

        var buffer = BuildBuffer(mapped, actual);
        var options = keepIdentity ? SqlBulkCopyOptions.KeepIdentity : SqlBulkCopyOptions.Default;
        var loaded = 0;
        using var transaction = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        // SqlBulkCopy takes its transaction at construction — it cannot be re-pointed.
        using var bulk = new SqlBulkCopy(conn, options, transaction)
        {
            DestinationTableName = $"[{schema}].[{table}]",
            BatchSize = BatchRows,
            BulkCopyTimeout = 300
        };
        foreach (DataColumn column in buffer.Columns)
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

        try
        {
            var line = 0;
            foreach (var record in EnumerateRecords(path, delimiter))
            {
                ct.ThrowIfCancellationRequested();
                line++;
                if (hasHeader && line == 1) continue;
                AddRow(buffer, record, mapped, line);
                loaded++;
                if (buffer.Rows.Count >= BatchRows)
                {
                    await bulk.WriteToServerAsync(buffer, ct);
                    progress?.Report($"{loaded:N0} row(s) sent…");
                    buffer.Rows.Clear();
                }
            }

            if (buffer.Rows.Count > 0) await bulk.WriteToServerAsync(buffer, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (created)
            {
                // The load failed before a single row landed, so the empty table this call
                // created is not the user's — take it back rather than leave a stray object.
                await using var drop = new SqlCommand($"DROP TABLE [{schema}].[{table}];", conn)
                { CommandTimeout = 30 };
                try { await drop.ExecuteNonQueryAsync(CancellationToken.None); }
                catch { AppLog.Warn($"import: could not drop {schema}.{table} after a failed load"); }
            }
            throw;
        }

        progress?.Report($"Loaded {loaded:N0} row(s) into {schema}.{table}.");
        return new ImportResult { RowsLoaded = loaded, TableCreated = created };
    }

    /// <summary>
    /// Fill one buffered row. Values are converted here rather than left to the server so a
    /// bad value names the file line that carries it.
    /// </summary>
    public static void AddRow(DataTable buffer, string?[] record, IReadOnlyList<ImportColumn> mapped, int line)
    {
        var row = buffer.NewRow();
        for (var i = 0; i < mapped.Count; i++)
        {
            var column = mapped[i];
            var name = column.TargetName.Trim();
            var raw = i < record.Length ? record[i] : null;
            if (string.IsNullOrEmpty(raw))
            {
                if (!column.Nullable)
                    throw new InvalidDataException(
                        $"Line {line}: [{name}] has no value and the mapping says NOT NULL.");
                row[name] = DBNull.Value;
                continue;
            }

            row[name] = ConvertValue(raw, column.SqlType, name, line);
        }
        buffer.Rows.Add(row);
    }

    /// <summary>
    /// Convert one field to the type its column claims. Surrounding spaces are stripped
    /// where they cannot be data (a number, a date, a GUID, a flag) and kept where they can
    /// be: a text column stores exactly what the file holds.
    /// </summary>
    private static object ConvertValue(string raw, string sqlType, string column, int line)
    {
        var text = raw.Trim();
        var root = Root(sqlType);
        try
        {
            return root switch
            {
                "bit" => BoolValues.Contains(text.ToLowerInvariant())
                    ? text.ToLowerInvariant() is "1" or "true" or "y" or "yes"
                    : throw new FormatException(),
                "tinyint" => byte.Parse(text, CultureInfo.InvariantCulture),
                "smallint" => short.Parse(text, CultureInfo.InvariantCulture),
                "int" => int.Parse(text, CultureInfo.InvariantCulture),
                "bigint" => long.Parse(text, CultureInfo.InvariantCulture),
                "decimal" or "numeric" or "money" or "smallmoney" =>
                    decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
                "float" or "real" => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
                "date" => DateTime.Parse(text, CultureInfo.InvariantCulture).Date,
                "datetime" or "datetime2" => DateTime.Parse(text, CultureInfo.InvariantCulture),
                "datetimeoffset" => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture),
                "time" => TimeSpan.Parse(text, CultureInfo.InvariantCulture),
                "uniqueidentifier" => Guid.Parse(text),
                "varbinary" => Convert.FromBase64String(text),
                _ => raw,
            };
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new InvalidDataException(
                $"Line {line}: '{Shorten(text)}' is not a valid {sqlType} for column [{column}].");
        }
    }

    private static string Root(string sqlType) => sqlType.Split('(')[0].Trim().ToLowerInvariant();

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "…";

    private static DataTable BuildBuffer(IReadOnlyList<ImportColumn> mapped, IReadOnlyList<TargetColumn> actual)
    {
        var buffer = new DataTable();
        foreach (var column in mapped)
        {
            var name = column.TargetName.Trim();
            var onServer = actual.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            buffer.Columns.Add(name, ClrType(onServer?.Type ?? column.SqlType));
        }
        return buffer;
    }

    private static Type ClrType(string sqlType) => Root(sqlType) switch
    {
        "bit" => typeof(bool),
        "tinyint" => typeof(byte),
        "smallint" => typeof(short),
        "int" => typeof(int),
        "bigint" => typeof(long),
        "decimal" or "numeric" or "money" or "smallmoney" => typeof(decimal),
        "float" or "real" => typeof(double),
        "date" or "datetime" or "datetime2" => typeof(DateTime),
        "datetimeoffset" => typeof(DateTimeOffset),
        "time" => typeof(TimeSpan),
        "uniqueidentifier" => typeof(Guid),
        _ => typeof(string),
    };

    private static string?[] Pad(string?[] record, int width) =>
        record.Length >= width ? record[..width]
            : record.Concat(Enumerable.Repeat<string?>(null, width - record.Length)).ToArray();
}
