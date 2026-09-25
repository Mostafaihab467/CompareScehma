using System.Text;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>Index shape captured by the New Index dialog.</summary>
public sealed class IndexSpec
{
    public string Schema { get; set; } = "";
    public string Table { get; set; } = "";
    public string IndexName { get; set; } = "";
    /// <summary>Nonclustered | Clustered | NonclusteredColumnstore | ClusteredColumnstore</summary>
    public string IndexType { get; set; } = "Nonclustered";
    public bool IsUnique { get; set; }
    public bool UseFillFactor { get; set; }
    public int FillFactor { get; set; }
    public string? Filter { get; set; }
    public List<string> KeyColumns { get; set; } = [];
    public List<string> IncludedColumns { get; set; } = [];

    /// <summary>Set by the dialog: true = run against the DB, false = script to the editor.</summary>
    public bool ExecuteNow { get; set; }
}

/// <summary>Partition shape captured by the Create Partition dialog.</summary>
public sealed class PartitionSpec
{
    public string Schema { get; set; } = "";
    public string Table { get; set; } = "";
    public string ColumnName { get; set; } = "";
    public string ColumnDataType { get; set; } = "";
    public string FunctionName { get; set; } = "";
    public string SchemeName { get; set; } = "";
    /// <summary>LEFT | RIGHT</summary>
    public string RangeType { get; set; } = "RIGHT";
    public List<string> Boundaries { get; set; } = [];
    /// <summary>Filegroup(s) the scheme points at; comma-separated for multiple.</summary>
    public string Filegroup { get; set; } = "PRIMARY";
    public bool AlignClusteredIndex { get; set; }
    /// <summary>Clustered PK/constraint index name when aligning an existing clustered index.</summary>
    public string? ExistingClusteredIndexName { get; set; }
    public List<string> ExistingClusteredKeyColumns { get; set; } = [];
    public bool TableIsHeap { get; set; }

    /// <summary>Set by the dialog: true = run against the DB, false = script to the editor.</summary>
    public bool ExecuteNow { get; set; }
}

/// <summary>
/// Builds the T-SQL behind DB Manager context actions. Pure string builders —
/// no DB access — so dialogs can live-preview the exact statement to be run.
/// </summary>
public static class ManagerScriptBuilder
{
    public static string Q(string ident) => "[" + ident.Replace("]", "]]") + "]";

    public static string Qual(string schema, string name) => $"{Q(schema)}.{Q(name)}";

    // ─── Index DDL ───────────────────────────────────────────────────────────

    public static string CreateIndex(IndexSpec s)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE ");
        if (s.IsUnique && s.IndexType is not ("NonclusteredColumnstore" or "ClusteredColumnstore"))
            sb.Append("UNIQUE ");

        switch (s.IndexType)
        {
            case "ClusteredColumnstore":
                sb.Append("CLUSTERED COLUMNSTORE INDEX ");
                sb.Append(Q(s.IndexName));
                sb.Append($" ON {Qual(s.Schema, s.Table)}");
                break;

            case "NonclusteredColumnstore":
                sb.Append("NONCLUSTERED COLUMNSTORE INDEX ");
                sb.Append(Q(s.IndexName));
                sb.Append($" ON {Qual(s.Schema, s.Table)}");
                sb.Append($" ({QuoteColumns(s.KeyColumns)})");
                break;

            case "Clustered":
            case "Nonclustered":
                sb.Append($"{s.IndexType.ToUpperInvariant()} INDEX ");
                sb.Append(Q(s.IndexName));
                sb.Append($" ON {Qual(s.Schema, s.Table)}");
                sb.Append($" ({QuoteColumns(s.KeyColumns)})");
                if (s.IncludedColumns.Count > 0)
                    sb.Append($" INCLUDE ({QuoteColumns(s.IncludedColumns)})");
                break;

            default:
                return $"-- Unknown index type '{s.IndexType}'";
        }

        if (!string.IsNullOrWhiteSpace(s.Filter) && s.IndexType is "Nonclustered" or "NonclusteredColumnstore")
            sb.Append($" WHERE {s.Filter.Trim()}");

        var with = new List<string>();
        if (s.UseFillFactor && s.FillFactor is > 0 and <= 100 && s.IndexType is not ("NonclusteredColumnstore" or "ClusteredColumnstore"))
            with.Add($"FILLFACTOR = {s.FillFactor}");
        if (with.Count > 0)
            sb.Append($" WITH ({string.Join(", ", with)})");

        sb.Append(';');
        return sb.ToString();
    }

    public static string RebuildIndex(string schema, string table, string index) =>
        $"ALTER INDEX {Q(index)} ON {Qual(schema, table)} REBUILD;";

    public static string DisableIndex(string schema, string table, string index) =>
        $"ALTER INDEX {Q(index)} ON {Qual(schema, table)} DISABLE;";

    /// <summary>Re-enabling a disabled index is a rebuild.</summary>
    public static string EnableIndex(string schema, string table, string index) =>
        $"ALTER INDEX {Q(index)} ON {Qual(schema, table)} REBUILD;";

    public static string DropIndex(string schema, string table, string index) =>
        $"DROP INDEX {Q(index)} ON {Qual(schema, table)};";

    public static string UpdateStatistics(string schema, string table, string? stat = null) =>
        stat is null or ""
            ? $"UPDATE STATISTICS {Qual(schema, table)};"
            : $"UPDATE STATISTICS {Qual(schema, table)} {Q(stat)};";

    // ─── Script As (SSMS-style templates) ────────────────────────────────────

    public static string SelectTop(string schema, string table, int top = 1000) =>
        $"SELECT TOP ({top}) *\nFROM {Qual(schema, table)}\n-- WHERE <predicate>\n-- ORDER BY <column>;";

    /// <summary>INSERT template: one NULL placeholder per column, commented with its name.</summary>
    public static string InsertTemplate(string schema, string table, IReadOnlyList<string> columns)
    {
        var cols = string.Join(", ", columns.Select(Q));
        var vals = string.Join(", ", columns.Select(c => $"/* {c} */ NULL"));
        return $"INSERT INTO {Qual(schema, table)} ({cols})\nVALUES ({vals});";
    }

    /// <summary>UPDATE template; keys default to the PK, else a deliberately false predicate.</summary>
    public static string UpdateTemplate(string schema, string table,
        IReadOnlyList<string> setColumns, IReadOnlyList<string> keyColumns)
    {
        var sets = string.Join(",\n    ", setColumns.Select(c => $"{Q(c)} = /* {c} */ NULL"));
        var where = WhereClause(keyColumns);
        return $"UPDATE {Qual(schema, table)}\nSET {sets}\nWHERE {where};";
    }

    public static string DeleteTemplate(string schema, string table, IReadOnlyList<string> keyColumns) =>
        $"DELETE FROM {Qual(schema, table)}\nWHERE {WhereClause(keyColumns)};";

    private static string WhereClause(IReadOnlyList<string> keyColumns) =>
        keyColumns.Count > 0
            ? string.Join("\n  AND ", keyColumns.Select(c => $"{Q(c)} = /* {c} */ NULL"))
            : "/* add the key predicate */ 1 = 0";

    /// <summary>EXEC template with one @param per procedure parameter (fill the NULLs).</summary>
    public static string ExecTemplate(string schema, string proc,
        IReadOnlyList<(string Name, string DataType)> parameters)
    {
        if (parameters.Count == 0)
            return $"EXEC {Qual(schema, proc)};";
        var args = string.Join(", ", parameters.Select(p => $"@{p.Name} = /* {p.DataType} */ NULL"));
        return $"EXEC {Qual(schema, proc)} {args};";
    }

    public static string DropObject(string schema, string name, string kindSqlName) =>
        $"DROP {kindSqlName} {Qual(schema, name)};";

    // ─── Database-level actions (root node) ──────────────────────────────────

    /// <summary>DBCC SHRINKDATABASE with a target % of free space to leave.</summary>
    public static string ShrinkDatabase(string database, int targetPercent = 10) =>
        $"DBCC SHRINKDATABASE ({Q(database)}, {targetPercent});";

    /// <summary>RESTORE over an existing database; executed from master context
    /// (see DbManagerService.RestoreDatabaseAsync) so the script itself is plain.</summary>
    public static string RestoreDatabase(string database, string backupPath) =>
        $"RESTORE DATABASE {Q(database)} FROM DISK = N'{backupPath.Replace("'", "''")}' WITH REPLACE, RECOVERY;";

    /// <summary>Turns an object's CREATE definition into CREATE OR ALTER.</summary>
    public static string ToCreateOrAlter(string definition) =>
        System.Text.RegularExpressions.Regex.Replace(
            definition,
            @"\bCREATE\s+(PROCEDURE|PROC|VIEW|FUNCTION|TRIGGER)\b",
            "CREATE OR ALTER $1",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(200));

    // ─── Partition DDL ───────────────────────────────────────────────────────

    /// <summary>
    /// Emits CREATE PARTITION FUNCTION + SCHEME, and optionally a clustered index
    /// rebuild that physically moves the table onto the scheme.
    /// </summary>
    public static string CreatePartition(PartitionSpec s)
    {
        var sb = new StringBuilder();
        var boundaries = string.Join(", ", s.Boundaries.Select(b => QuoteBoundary(s.ColumnDataType, b)));
        sb.AppendLine($"CREATE PARTITION FUNCTION {Q(s.FunctionName)} ({s.ColumnDataType})");
        sb.AppendLine($"    AS RANGE {s.RangeType.ToUpperInvariant()} FOR VALUES ({boundaries});");
        sb.AppendLine();
        sb.AppendLine($"CREATE PARTITION SCHEME {Q(s.SchemeName)}");
        sb.AppendLine($"    AS PARTITION {Q(s.FunctionName)}");
        var fgs = s.Filegroup.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        sb.AppendLine($"    ALL TO ({string.Join(", ", fgs.Select(Q))});");

        if (s.AlignClusteredIndex && !string.IsNullOrEmpty(s.ExistingClusteredIndexName))
        {
            sb.AppendLine();
            sb.AppendLine("-- Align the existing clustered index onto the partition scheme");
            sb.AppendLine($"CREATE UNIQUE CLUSTERED INDEX {Q(s.ExistingClusteredIndexName)}");
            sb.AppendLine($"    ON {Qual(s.Schema, s.Table)} ({QuoteColumns(s.ExistingClusteredKeyColumns)})");
            sb.AppendLine($"    WITH (DROP_EXISTING = ON) ON {Q(s.SchemeName)} ({Q(s.ColumnName)});");
        }
        else if (s.AlignClusteredIndex && s.TableIsHeap)
        {
            var cix = $"CIX_{Sanitize(s.Table)}_{Sanitize(s.ColumnName)}";
            sb.AppendLine();
            sb.AppendLine("-- Table is a heap — create a clustered index on the partition column");
            sb.AppendLine($"CREATE CLUSTERED INDEX {Q(cix)}");
            sb.AppendLine($"    ON {Qual(s.Schema, s.Table)} ({Q(s.ColumnName)})");
            sb.AppendLine($"    ON {Q(s.SchemeName)} ({Q(s.ColumnName)});");
        }

        return sb.ToString().TrimEnd();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string QuoteColumns(IEnumerable<string> cols) =>
        string.Join(", ", cols.Select(c => Q(c)));

    private static string Sanitize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray());

    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    { "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "money", "smallmoney", "float", "real" };

    /// <summary>Numbers stay raw; everything else becomes an N'…' literal.</summary>
    private static string QuoteBoundary(string dataType, string value)
    {
        if (NumericTypes.Contains(dataType))
        {
            if (decimal.TryParse(value, out _)) return value;
            throw new FormatException($"'{value}' is not a valid number for column type {dataType}.");
        }
        return "N'" + value.Replace("'", "''") + "'";
    }
}
