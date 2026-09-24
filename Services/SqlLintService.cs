using System.Text.RegularExpressions;

namespace SchemaCompare.Services;

public readonly record struct SqlLintIssue(int Start, int Length, string Message);

/// <summary>
/// Lightweight T-SQL diagnostics for the query editor: unmatched delimiters,
/// common typos, unknown tables, and qualified / unqualified unknown columns.
/// Verified against the connected database schema cache.
/// </summary>
public static class SqlLintService
{
    private static readonly Regex TableRefRegex = new(
        @"\b(?:FROM|JOIN|INTO|UPDATE|TRUNCATE\s+TABLE|DELETE\s+FROM)\s+(?<t>\[?[\w#$]+\]?(?:\s*\.\s*\[?[\w#$]+\]?)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AliasRegex = new(
        @"\b(?:FROM|JOIN)\s+(?<t>\[?[\w#$]+\]?(?:\s*\.\s*\[?[\w#$]+\]?)?)\s+(?:AS\s+)?(?<a>\[?[\w#$]+\]?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex QualifiedColRegex = new(
        @"(?<a>\[[\w#$]+\]|[\w#$]+)\s*\.\s*(?<c>\[[\w#$]+\]|[\w#$]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AsAliasRegex = new(
        @"\bAS\s+(?<a>\[[\w#$]+\]|[A-Za-z_][A-Za-z0-9_#$]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Typos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SELCT"] = "SELECT", ["SLECT"] = "SELECT", ["SEELCT"] = "SELECT", ["SELEST"] = "SELECT", ["SELETC"] = "SELECT",
        ["FORM"] = "FROM", ["FRM"] = "FROM",
        ["WHRE"] = "WHERE", ["WHER"] = "WHERE", ["WEHRE"] = "WHERE", ["WHEREE"] = "WHERE",
        ["UDPATE"] = "UPDATE", ["UPATE"] = "UPDATE",
        ["DELET"] = "DELETE", ["DELTETE"] = "DELETE",
        ["INERT"] = "INSERT", ["INSER"] = "INSERT",
        ["GRUP"] = "GROUP", ["HAVNG"] = "HAVING", ["HAVINGG"] = "HAVING",
        ["ODER"] = "ORDER", ["JOIM"] = "JOIN", ["JION"] = "JOIN",
        ["CREAT"] = "CREATE", ["CRATE"] = "CREATE",
        ["ALETR"] = "ALTER", ["ATRUNCATE"] = "TRUNCATE",
        ["PROCEDUR"] = "PROCEDURE", ["EXECUET"] = "EXECUTE",
        ["DISTINCTT"] = "DISTINCT", ["DITINCT"] = "DISTINCT",
        ["TRANSACTOIN"] = "TRANSACTION", ["BEGGIN"] = "BEGIN", ["COMMTI"] = "COMMIT", ["ROLLBCK"] = "ROLLBACK"
    };

    private static readonly HashSet<string> SkipTableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "VALUES", "OPENJSON", "OPENQUERY", "OPENROWSET", "OPENDATASOURCE",
        "STRING_SPLIT", "STRING_AGG", "SYS", "INFORMATION_SCHEMA", "INSERTED", "DELETED"
    };

    private static readonly HashSet<string> KnownSqlWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Keywords
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "LIKE", "BETWEEN",
        "IS", "NULL", "AS", "CASE", "WHEN", "THEN", "ELSE", "END", "GROUP",
        "BY", "HAVING", "ORDER", "ASC", "DESC", "TOP", "DISTINCT", "JOIN",
        "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "ON", "INSERT", "INTO",
        "VALUES", "UPDATE", "SET", "DELETE", "CREATE", "ALTER", "DROP", "TABLE",
        "VIEW", "PROCEDURE", "PROC", "FUNCTION", "RETURNS", "TRIGGER", "DATABASE",
        "INDEX", "CLUSTERED", "NONCLUSTERED", "PRIMARY", "KEY", "FOREIGN",
        "REFERENCES", "CONSTRAINT", "DEFAULT", "CHECK", "UNIQUE", "WITH",
        "UNION", "ALL", "EXCEPT", "INTERSECT", "EXEC", "EXECUTE", "BEGIN",
        "COMMIT", "ROLLBACK", "TRAN", "TRANSACTION", "DECLARE", "IF", "WHILE",
        "BREAK", "CONTINUE", "RETURN", "WAITFOR", "DELAY", "TRY", "CATCH",
        "THROW", "PRINT", "USE", "SCHEMA", "GO", "OVER", "PARTITION", "ROWS",
        "RANGE", "UNBOUNDED", "PRECEDING", "FOLLOWING", "CURRENT", "ROW", "ONLY",
        "FETCH", "NEXT", "OFFSET", "OPTION", "RECOMPILE", "MAXDOP", "HASH",
        "LOOP", "MERGE", "OUTPUT", "APPLY", "PIVOT", "UNPIVOT", "NOLOCK",
        "HOLDLOCK", "UPDLOCK", "XLOCK", "TABLOCK", "TABLOCKX", "SERIALIZABLE",
        "SNAPSHOT", "REPEATABLE", "READ", "COMMITTED", "UNCOMMITTED", "SOME",
        "ANY", "EXISTS", "FOR", "XML", "PATH", "RAW", "AUTO", "ELEMENTS",
        "TYPE", "ROOT", "SYSTEM_VERSIONING", "TEMPORAL", "OUT", "VARYING",
        "RECONFIGURE", "IDENTITY", "IDENTITY_INSERT", "NOCHECK", "CHECKPOINT",
        "OFF", "STATISTICS", "IO", "TIME", "PROFILE", "PLAN", "FORCE", "OPTIMIZE",
        "UNKNOWN", "COLLATE", "ESCAPE", "OPENJSON", "OPENQUERY", "OPENROWSET",
        "OPENDATASOURCE", "STRING_SPLIT", "STRING_AGG", "CHECKSUM_AGG",
        // Functions
        "COUNT", "SUM", "AVG", "MIN", "MAX", "COUNT_BIG", "STDEV", "STDEVP",
        "VAR", "VARP", "GETDATE", "GETUTCDATE", "SYSDATETIME", "SYSUTCDATETIME",
        "SYSDATETIMEOFFSET", "CURRENT_TIMESTAMP", "DATEDIFF", "DATEDIFF_BIG",
        "DATEADD", "DATEPART", "DATENAME", "DAY", "MONTH", "YEAR", "EOMONTH",
        "ISDATE", "DATEFROMPARTS", "DATETIMEFROMPARTS", "DATETIME2FROMPARTS",
        "TIMEFROMPARTS", "SWITCHOFFSET", "TODATETIMEOFFSET", "ISNULL", "COALESCE",
        "NULLIF", "IIF", "CHOOSE", "CAST", "CONVERT", "TRY_CAST", "TRY_CONVERT",
        "PARSE", "TRY_PARSE", "LEN", "SUBSTRING", "CHARINDEX", "PATINDEX",
        "REPLACE", "REPLICATE", "REVERSE", "RTRIM", "LTRIM", "TRIM", "LOWER",
        "UPPER", "LEFT", "RIGHT", "CONCAT", "CONCAT_WS", "FORMAT", "SOUNDEX",
        "SPACE", "STR", "STUFF", "TRANSLATE", "UNICODE", "ASCII", "CHAR",
        "NCHAR", "DIFFERENCE", "STRING_ESCAPE", "QUOTENAME", "NEWID",
        "NEWSEQUENTIALID", "CHECKSUM", "BINARY_CHECKSUM", "HASHBYTES",
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LEAD", "LAG",
        "FIRST_VALUE", "LAST_VALUE", "CUME_DIST", "PERCENT_RANK",
        "PERCENTILE_CONT", "PERCENTILE_DISC", "ABS", "ACOS", "ASIN", "ATAN",
        "ATN2", "CEILING", "COS", "COT", "DEGREES", "EXP", "FLOOR", "LOG",
        "LOG10", "PI", "POWER", "RADIANS", "RAND", "ROUND", "SIGN", "SIN",
        "SQRT", "SQUARE", "TAN", "ISNUMERIC", "DB_NAME", "DB_ID", "OBJECT_ID",
        "OBJECT_NAME", "OBJECT_SCHEMA_NAME", "SCHEMA_ID", "SCHEMA_NAME",
        "USER_NAME", "USER_ID", "SUSER_NAME", "SUSER_SNAME", "SUSER_ID",
        "ORIGINAL_DB_NAME", "PARSENAME", "SCOPE_IDENTITY", "IDENT_CURRENT",
        "IDENT_INCR", "IDENT_SEED", "COL_LENGTH", "COL_NAME", "COLUMNPROPERTY",
        "DATABASEPROPERTYEX", "OBJECTPROPERTY", "OBJECTPROPERTYEX",
        "OBJECT_DEFINITION", "SESSION_CONTEXT", "XACT_STATE", "ERROR_NUMBER",
        "ERROR_MESSAGE", "ERROR_LINE", "ERROR_SEVERITY", "ERROR_STATE",
        "ERROR_PROCEDURE", "RAISERROR",
        // Types
        "BIGINT", "BINARY", "BIT", "CHAR", "DATE", "DATETIME", "DATETIME2",
        "DATETIMEOFFSET", "DECIMAL", "FLOAT", "GEOGRAPHY", "GEOMETRY",
        "HIERARCHYID", "IMAGE", "INT", "INTEGER", "MONEY", "NCHAR", "NTEXT",
        "NUMERIC", "NVARCHAR", "REAL", "ROWVERSION", "SMALLDATETIME", "SMALLINT",
        "SMALLMONEY", "SQL_VARIANT", "SYSNAME", "TEXT", "TIME", "TIMESTAMP",
        "TINYINT", "UNIQUEIDENTIFIER", "VARBINARY", "VARCHAR", "XML",
        // Common schemas & pseudo tables
        "DBO", "SYS", "INFORMATION_SCHEMA", "GUEST", "INSERTED", "DELETED"
    };

    public static List<SqlLintIssue> Analyze(
        string sql,
        IReadOnlyList<QuerySchemaService.TableInfo>? tables,
        IReadOnlyDictionary<string, List<string>>? columnsByTable)
    {
        var issues = new List<SqlLintIssue>();
        if (string.IsNullOrEmpty(sql)) return issues;

        ScanDelimiters(sql, issues);
        ScanTyposAndGo(sql, issues);

        if (tables is { Count: > 0 })
            ScanSchema(sql, tables, columnsByTable, issues);

        issues.Sort((a, b) => a.Start.CompareTo(b.Start));
        return issues;
    }

    private static void ScanDelimiters(string sql, List<SqlLintIssue> issues)
    {
        var paren = 0;
        var bracket = 0;
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var stringStart = 0;
        var blockStart = 0;
        var lastOpenParen = -1;
        var lastOpenBracket = -1;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (c is '\n' or '\r') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }
            if (inString)
            {
                if (c == '\'' && next == '\'')
                {
                    i++;
                    continue;
                }
                if (c == '\'')
                    inString = false;
                continue;
            }

            if (c == '-' && next == '-')
            {
                inLineComment = true;
                i++;
                continue;
            }
            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                blockStart = i;
                i++;
                continue;
            }
            if (c == '\'')
            {
                inString = true;
                stringStart = i;
                continue;
            }

            if (c == '(')
            {
                paren++;
                lastOpenParen = i;
            }
            else if (c == ')')
            {
                paren--;
                if (paren < 0)
                {
                    issues.Add(new SqlLintIssue(i, 1, "Unmatched closing parenthesis."));
                    paren = 0;
                }
            }
            else if (c == '[')
            {
                bracket++;
                lastOpenBracket = i;
            }
            else if (c == ']')
            {
                bracket--;
                if (bracket < 0)
                {
                    issues.Add(new SqlLintIssue(i, 1, "Unmatched closing bracket."));
                    bracket = 0;
                }
            }
        }

        if (inString)
            issues.Add(new SqlLintIssue(stringStart, Math.Max(1, sql.Length - stringStart),
                "Unclosed string literal — missing closing quote (')."));
        if (inBlockComment)
            issues.Add(new SqlLintIssue(blockStart, Math.Max(2, sql.Length - blockStart),
                "Unclosed block comment — missing */."));
        if (paren > 0 && lastOpenParen >= 0)
            issues.Add(new SqlLintIssue(lastOpenParen, 1, "Unclosed parenthesis."));
        if (bracket > 0 && lastOpenBracket >= 0)
            issues.Add(new SqlLintIssue(lastOpenBracket, 1, "Unclosed identifier bracket ([)."));
    }

    private static void ScanTyposAndGo(string sql, List<SqlLintIssue> issues)
    {
        var lineStart = 0;
        for (var i = 0; i <= sql.Length; i++)
        {
            if (i < sql.Length && sql[i] is not ('\n' or '\r')) continue;
            var line = sql[lineStart..i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("GO", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Length > 2 &&
                char.IsLetterOrDigit(trimmed[2]))
            {
                var goAt = lineStart + line.IndexOf("GO", StringComparison.OrdinalIgnoreCase);
                issues.Add(new SqlLintIssue(goAt, 2, "GO must be on its own line (batch separator)."));
            }
            lineStart = i + 1;
            if (i < sql.Length && sql[i] == '\r' && i + 1 < sql.Length && sql[i + 1] == '\n')
            {
                i++;
                lineStart = i + 1;
            }
        }

        foreach (Match m in Regex.Matches(sql, @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
        {
            if (Typos.TryGetValue(m.Value, out var correct))
                issues.Add(new SqlLintIssue(m.Index, m.Length,
                    $"Unknown keyword '{m.Value}'. Did you mean {correct}?"));
        }
    }

    private static void ScanSchema(
        string sql,
        IReadOnlyList<QuerySchemaService.TableInfo> tables,
        IReadOnlyDictionary<string, List<string>>? columnsByTable,
        List<SqlLintIssue> issues)
    {
        var stripped = StripStringsAndComments(sql);
        var cteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(stripped, @"\bWITH\s+(\[?[\w#$]+\]?)\s+AS\s*\(", RegexOptions.IgnoreCase))
            cteNames.Add(Unwrap(m.Groups[1].Value));

        var tableLookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables)
        {
            tableLookup.Add(t.Name);
            tableLookup.Add(t.ShortName);
            tableLookup.Add($"{t.Schema}.{t.Name}");
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AliasRegex.Matches(stripped))
        {
            var alias = Unwrap(m.Groups["a"].Value);
            if (SkipTableNames.Contains(alias) || string.Equals(alias, "AS", StringComparison.OrdinalIgnoreCase))
                continue;
            aliases[alias] = Unwrap(m.Groups["t"].Value);
        }

        var referencedTableKeys = new List<string>();
        var tableRefSpans = new List<(int Start, int End)>();

        foreach (Match m in TableRefRegex.Matches(stripped))
        {
            var raw = m.Groups["t"].Value;
            var name = Unwrap(raw);
            if (name.Length == 0 || name[0] is '#' or '@') continue;
            if (SkipTableNames.Contains(name)) continue;

            tableRefSpans.Add((m.Groups["t"].Index, m.Groups["t"].Index + m.Groups["t"].Length));

            var shortName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
            if (cteNames.Contains(shortName)) continue;

            if (tableLookup.Contains(name) || tableLookup.Contains(shortName))
            {
                if (columnsByTable != null)
                {
                    var resolved = ResolveTableKey(raw, tables, columnsByTable);
                    if (!referencedTableKeys.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                        referencedTableKeys.Add(resolved);
                }
                continue;
            }

            var idx = m.Groups["t"].Index;
            var len = Math.Max(1, m.Groups["t"].Length);
            issues.Add(new SqlLintIssue(idx, len, $"Unknown table or view '{name}'."));
        }

        if (columnsByTable is null || columnsByTable.Count == 0) return;

        var handledSpans = new List<(int Start, int End)>();

        // 1. Validate qualified columns: alias.col or schema.table.col
        foreach (Match m in QualifiedColRegex.Matches(stripped))
        {
            var alias = Unwrap(m.Groups["a"].Value);
            var col = Unwrap(m.Groups["c"].Value);
            if (alias.Length == 0 || col.Length == 0) continue;
            if (string.Equals(alias, "dbo", StringComparison.OrdinalIgnoreCase)) continue;
            if (SkipTableNames.Contains(alias)) continue;

            handledSpans.Add((m.Index, m.Index + m.Length));

            string? tableKey = null;
            if (aliases.TryGetValue(alias, out var tableRef))
                tableKey = ResolveTableKey(tableRef, tables, columnsByTable);
            else
                tableKey = ResolveTableKey(alias, tables, columnsByTable);

            if (tableKey == null) continue;
            if (!columnsByTable.TryGetValue(tableKey, out var cols)) continue;
            if (cols.Any(c => string.Equals(c, col, StringComparison.OrdinalIgnoreCase))) continue;

            issues.Add(new SqlLintIssue(m.Groups["c"].Index, m.Groups["c"].Length,
                $"Unknown column '{col}' on '{tableKey}'."));
        }

        // 2. Validate unqualified columns if at least one database table is referenced
        if (referencedTableKeys.Count == 0) return;

        var allValidCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tk in referencedTableKeys)
        {
            if (columnsByTable.TryGetValue(tk, out var cList))
            {
                foreach (var c in cList)
                    allValidCols.Add(c);
            }
        }

        // Also ignore aliases defined via "AS alias"
        var definedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in aliases.Keys) definedAliases.Add(a);
        foreach (Match m in AsAliasRegex.Matches(stripped))
        {
            definedAliases.Add(Unwrap(m.Groups["a"].Value));
            handledSpans.Add((m.Groups["a"].Index, m.Groups["a"].Index + m.Groups["a"].Length));
        }

        // Scan all identifiers in the SQL query
        foreach (Match m in Regex.Matches(stripped, @"\[(?<c>[\w#$]+)\]|\b(?<c>[A-Za-z_][A-Za-z0-9_#$]*)\b"))
        {
            var word = m.Groups["c"].Value;
            var start = m.Groups["c"].Index;
            var end = start + m.Groups["c"].Length;

            // Skip if inside a qualified col span or table reference span
            if (handledSpans.Any(s => start >= s.Start && end <= s.End)) continue;
            if (tableRefSpans.Any(s => start >= s.Start && end <= s.End)) continue;

            // Skip keywords, functions, types, schemas, CTEs, aliases, variables, numbers
            if (KnownSqlWords.Contains(word)) continue;
            if (definedAliases.Contains(word)) continue;
            if (cteNames.Contains(word)) continue;
            if (tableLookup.Contains(word)) continue;
            if (word.StartsWith('@') || word.StartsWith('#')) continue;

            // Check if it is a valid column on any of the referenced tables
            if (allValidCols.Contains(word)) continue;

            // Unknown column detected!
            if (referencedTableKeys.Count == 1)
            {
                issues.Add(new SqlLintIssue(start, m.Groups["c"].Length,
                    $"Unknown column '{word}' on '{referencedTableKeys[0]}'."));
            }
            else
            {
                issues.Add(new SqlLintIssue(start, m.Groups["c"].Length,
                    $"Unknown column '{word}'."));
            }
        }
    }

    private static string ResolveTableKey(
        string raw,
        IReadOnlyList<QuerySchemaService.TableInfo> tables,
        IReadOnlyDictionary<string, List<string>> columnsByTable)
    {
        var name = Unwrap(raw);
        var shortName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
        foreach (var t in tables)
        {
            if (string.Equals(t.Name, shortName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.ShortName, name, StringComparison.OrdinalIgnoreCase))
                return $"{t.Schema}.{t.Name}";
        }
        foreach (var key in columnsByTable.Keys)
            if (key.EndsWith("." + shortName, StringComparison.OrdinalIgnoreCase))
                return key;
        return shortName;
    }

    private static string Unwrap(string raw) =>
        raw.Trim().Replace("[", "").Replace("]", "").Replace(" ", "");

    /// <summary>Replace string/comment contents with spaces so regex offsets stay aligned.</summary>
    private static string StripStringsAndComments(string sql)
    {
        var chars = sql.ToCharArray();
        var inLine = false;
        var inBlock = false;
        var inString = false;
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';
            if (inLine)
            {
                if (c is '\n' or '\r') inLine = false;
                else if (!char.IsWhiteSpace(c)) chars[i] = ' ';
                continue;
            }
            if (inBlock)
            {
                if (c == '*' && next == '/')
                {
                    chars[i] = chars[i + 1] = ' ';
                    inBlock = false;
                    i++;
                }
                else if (!char.IsWhiteSpace(c)) chars[i] = ' ';
                continue;
            }
            if (inString)
            {
                if (c == '\'' && next == '\'')
                {
                    chars[i] = chars[i + 1] = ' ';
                    i++;
                    continue;
                }
                if (c == '\'')
                {
                    inString = false;
                    continue;
                }
                if (!char.IsWhiteSpace(c)) chars[i] = ' ';
                continue;
            }
            if (c == '-' && next == '-') { inLine = true; chars[i] = chars[i + 1] = ' '; i++; continue; }
            if (c == '/' && next == '*') { inBlock = true; chars[i] = chars[i + 1] = ' '; i++; continue; }
            if (c == '\'') { inString = true; continue; }
        }
        return new string(chars);
    }
}
