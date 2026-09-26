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
        // sys.parameters hands back the name already carrying its '@'; a template that adds the
        // sign must not double it, or the script asks for a parameter named "@@Days".
        var args = string.Join(", ",
            parameters.Select(p => $"@{p.Name.TrimStart('@')} = /* {p.DataType} */ NULL"));
        return $"EXEC {Qual(schema, proc)} {args};";
    }

    public static string DropObject(string schema, string name, string kindSqlName) =>
        $"DROP {kindSqlName} {Qual(schema, name)};";

    // ─── Database-level actions (root node) ──────────────────────────────────

    /// <summary>DBCC SHRINKDATABASE with a target % of free space to leave.</summary>
    public static string ShrinkDatabase(string database, int targetPercent = 10) =>
        $"DBCC SHRINKDATABASE ({Q(database)}, {targetPercent});";

    /// <summary>
    /// The RESTORE statement for a plan. Relocated files become MOVE clauses, an
    /// in-place overwrite only happens when REPLACE was explicitly opted into, and
    /// STOPAT is emitted last so the option list stays readable.
    /// </summary>
    public static string RestoreDatabase(RestorePlan plan)
    {
        plan.Validate();

        var options = new List<string> { $"FILE = {plan.SetPosition}" };
        foreach (var file in plan.Files)
        {
            if (plan.Moves.TryGetValue(file.LogicalName, out var target) &&
                !string.IsNullOrWhiteSpace(target) &&
                !string.Equals(target, file.PhysicalName, StringComparison.OrdinalIgnoreCase))
                options.Add($"MOVE {Lit(file.LogicalName)} TO {Lit(target)}");
        }

        if (plan.ReplaceExisting) options.Add("REPLACE");
        options.Add(plan.NoRecovery ? "NORECOVERY" : "RECOVERY");
        if (plan.StopAt is { } stopAt) options.Add($"STOPAT = N'{stopAt:yyyy-MM-dd HH:mm:ss}'");
        options.Add("STATS = 5");

        return
            $"RESTORE DATABASE {Q(plan.TargetDatabase)}\n    FROM DISK = {Lit(plan.BackupPath)}\n" +
            $"    WITH {string.Join(",\n         ", options)};";
    }

    /// <summary>Where a restored file should land when relocating to the instance's
    /// default folder. Renaming with the target database keeps side-by-side restores
    /// from colliding with the source database's own files.</summary>
    public static string SuggestedPath(BackupFileInfo file, string directory, string targetDatabase)
    {
        var dir = directory.TrimEnd('\\', '/');
        var original = Path.GetFileName(file.PhysicalName);
        var extension = Path.GetExtension(original);
        if (string.IsNullOrEmpty(extension))
            extension = file.IsLog ? ".ldf" : ".mdf";
        var stem = Path.GetFileNameWithoutExtension(original);
        return $@"{dir}\{targetDatabase}_{stem}{extension}";
    }

    private static string Lit(string value) => "N'" + value.Replace("'", "''") + "'";

    /// <summary>
    /// BACKUP DATABASE / BACKUP LOG written by the server to its own disk. Without
    /// OverwriteMedia the statement appends a new backup set, which is what lets the
    /// restore dialog offer a set picker instead of a single opaque file.
    /// </summary>
    public static string BackupDatabase(BackupRequest r)
    {
        r.Validate();

        var options = new List<string>();
        if (r.CopyOnly) options.Add("COPY_ONLY");
        options.Add(r.Compress ? "COMPRESSION" : "NO_COMPRESSION");
        if (r.Checksum) options.Add("CHECKSUM");
        else options.Add("NO_CHECKSUM");
        if (r.OverwriteMedia) options.Add("FORMAT, INIT");
        if (!string.IsNullOrWhiteSpace(r.Description)) options.Add($"DESCRIPTION = {Lit(r.Description!)}");
        options.Add($"STATS = {r.StatsPercent}");

        var verb = r.LogBackup ? "BACKUP LOG" : "BACKUP DATABASE";
        return $"{verb} {Q(r.Database)}\n    TO DISK = {Lit(r.FilePath)}\n" +
               $"    WITH {string.Join(",\n         ", options)};";
    }

    /// <summary>RESTORE VERIFYONLY, so a backup nobody checked is not a backup.</summary>
    public static string VerifyBackup(string filePath, bool checksum) =>
        $"RESTORE VERIFYONLY FROM DISK = {Lit(filePath)}" + (checksum ? " WITH CHECKSUM;" : ";");

    /// <summary>File name used by the backup dialog; log backups get a .trn extension.</summary>
    public static string SuggestedBackupFileName(string database, DateTimeOffset at, bool logBackup) =>
        $"{database.Replace(' ', '_')}_{at:yyyyMMdd_HHmm}{(logBackup ? ".trn" : ".bak")}";

    // ─── SQL Agent jobs ──────────────────────────────────────────────────────

    /// <summary>Agent procedures are called with a name, not a job_id, so the name has
    /// to survive being quoted into a literal.</summary>
    private static string JobLiteral(string job)
    {
        if (string.IsNullOrWhiteSpace(job))
            throw new InvalidOperationException("Select a SQL Agent job first.");
        if (job.Contains('\0'))
            throw new InvalidOperationException("The job name contains invalid characters.");
        return Lit(job.Trim());
    }

    public static string StartAgentJob(string job) =>
        $"EXECUTE msdb.dbo.sp_start_job @job_name = {JobLiteral(job)};";

    public static string SetAgentJobEnabled(string job, bool enabled) =>
        $"EXECUTE msdb.dbo.sp_update_job @job_name = {JobLiteral(job)}, @enabled = {(enabled ? 1 : 0)};";

    // ─── Query Store ─────────────────────────────────────────────────────────

    /// <summary>Pins one plan to one query. Both ids come out of Query Store itself, so
    /// they are checked as positive integers here and no free text reaches the call.</summary>
    public static string ForceQueryPlan(long queryId, long planId)
    {
        PositiveId(queryId, "query id");
        PositiveId(planId, "plan id");
        return $"EXECUTE sys.sp_query_store_force_plan @query_id = {queryId}, @plan_id = {planId};";
    }

    /// <summary>Releases a pinned plan. The documentation names only <c>@query_id</c>, but
    /// the procedure demands the plan too and fails with "insufficient number of arguments"
    /// without it, so both ids are always sent.</summary>
    public static string UnforceQueryPlan(long queryId, long planId)
    {
        PositiveId(queryId, "query id");
        PositiveId(planId, "plan id");
        return $"EXECUTE sys.sp_query_store_unforce_plan @query_id = {queryId}, @plan_id = {planId};";
    }

    /// <summary>Turning Query Store on is a database-wide setting, so the script spells out
    /// the limits it is being given instead of a bare <c>= ON</c> nobody can review.</summary>
    public static string EnableQueryStore(string database)
    {
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("Name the database to enable Query Store on.");
        return
            $"ALTER DATABASE {Q(database.Trim())} SET QUERY_STORE = ON\n" +
            "(\n" +
            "    OPERATION_MODE = READ_WRITE,\n" +
            "    QUERY_CAPTURE_MODE = AUTO,\n" +
            "    MAX_STORAGE_SIZE_MB = 1024,\n" +
            "    INTERVAL_LENGTH_MINUTES = 60,\n" +
            "    DATA_FLUSH_INTERVAL_SECONDS = 900,\n" +
            "    CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30),\n" +
            "    SIZE_BASED_CLEANUP_MODE = AUTO,\n" +
            "    MAX_PLANS_PER_QUERY = 200\n" +
            ");";
    }

    private static void PositiveId(long value, string what)
    {
        if (value <= 0)
            throw new InvalidOperationException($"A {what} must be a positive number; {value} is not one.");
    }


    /// <summary>The batches to run, in order. An in-place overwrite of a live database
    /// needs exclusive access first, and only gains it back once the restore recovers.</summary>
    public static List<string> RestoreBatches(RestorePlan plan, bool targetExists)
    {
        var batches = new List<string>();
        var inPlace = targetExists && plan.IsInPlaceRestore;
        if (inPlace)
            batches.Add($"ALTER DATABASE {Q(plan.TargetDatabase)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;");
        batches.Add(RestoreDatabase(plan));
        if (inPlace && !plan.NoRecovery)
            batches.Add($"ALTER DATABASE {Q(plan.TargetDatabase)} SET MULTI_USER;");
        return batches;
    }

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

    // ─── Object designer DDL ─────────────────────────────────────────────────

    /// <summary>
    /// Builds the CREATE statement behind the New Table / View / Stored Procedure
    /// designer. Invalid definitions are refused here so the preview, the confirmation
    /// and the executed text are all the same valid script.
    /// </summary>
    public static string CreateObject(DesignerSpec s)
    {
        if (string.IsNullOrWhiteSpace(s.Name))
            throw new InvalidOperationException("Give the object a name.");
        var full = Qual(s.Schema.Trim(), s.Name.Trim());

        return s.Kind switch
        {
            DesignerKind.Table => CreateTableScript(full, s),
            DesignerKind.View => CreateBodyScript("VIEW", full, s.Body, "AS"),
            DesignerKind.StoredProcedure => CreateBodyScript("PROCEDURE", full, s.Body, ""),
            _ => throw new InvalidOperationException($"Unknown object kind {s.Kind}."),
        };
    }

    private static string CreateTableScript(string full, DesignerSpec s)
    {
        var table = s.Name.Trim();
        var lines = new List<string>();
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in s.Columns)
        {
            var name = c.Name.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(c.Type))
                throw new InvalidOperationException("Every column needs a name and a type.");
            if (!seen.Add(name))
                throw new InvalidOperationException($"Column [{name}] is listed twice.");
            if (!IsNumericType(c.Type) && c.IsIdentity)
                throw new InvalidOperationException(
                    $"[{name}] is {c.Type.Trim()}; only a numeric column can be an identity.");

            var parts = new List<string> { Q(name), c.Type.Trim() };
            if (c.IsIdentity)
            {
                parts.Add("IDENTITY(1, 1)");
                parts.Add("NOT NULL");
            }
            else
            {
                parts.Add(c.IsKey ? "NOT NULL" : c.Nullable ? "NULL" : "NOT NULL");
            }
            lines.Add(string.Join(" ", parts));

            if (c.IsKey) keys.Add(Q(name));
            if (!string.IsNullOrWhiteSpace(c.Default))
                lines.Add($"CONSTRAINT {Q($"DF_{Sanitize(table)}_{Sanitize(name)}")} " +
                          $"DEFAULT ({DefaultLiteral(c.Default)}) FOR {Q(name)}");
        }

        if (lines.Count == 0)
            throw new InvalidOperationException("A table needs at least one column.");
        if (keys.Count > 0)
            lines.Add($"CONSTRAINT {Q($"PK_{Sanitize(table)}")} PRIMARY KEY CLUSTERED ({string.Join(", ", keys)})");

        var sb = new StringBuilder();
        sb.AppendLine($"IF OBJECT_ID(N'{Unquote(full)}', N'U') IS NULL");
        sb.AppendLine($"CREATE TABLE {full}");
        sb.AppendLine("(");
        sb.AppendLine(string.Join($",{Environment.NewLine}",
            lines.Select(l => $"    {l}")));
        sb.Append(");");
        return sb.ToString();
    }

    private static string CreateBodyScript(string keyword, string full, string body, string separator)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException($"A {keyword.ToLowerInvariant()} needs a definition.");
        var indented = body.Trim().Replace("\n", "\n    ", StringComparison.Ordinal);
        var head = separator.Length == 0
            ? $"CREATE OR ALTER {keyword} {full}"
            : $"CREATE OR ALTER {keyword} {full}\n    {separator}";
        return $"{head}\n    {indented}";
    }

    /// <summary>OBJECT_ID takes an unbracketed two-part name.</summary>
    private static string Unquote(string bracketed) => bracketed.Replace("]", "").Replace("[", "");

    private static bool IsNumericType(string type) =>
        NumericTypes.Contains(type.Trim().Split('(')[0].Trim());

    /// <summary>
    /// A default is a T-SQL expression, not a string: numbers, function calls and text the
    /// operator already quoted pass through, anything else becomes an N'…' literal.
    /// </summary>
    private static string DefaultLiteral(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith('N') && text.Length > 2 && text[1] == '\''
            || text.StartsWith('\'') || text.StartsWith('(')
            || text.Contains('(') || double.TryParse(text, out _))
            return text;
        return "N'" + text.Replace("'", "''") + "'";
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
