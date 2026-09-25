using System.Text;
using System.Text.RegularExpressions;

namespace SchemaCompare.Services;

/// <summary>Structured plain-English analysis for the explain dialog.</summary>
public sealed class SqlExplanation
{
    /// <summary>One-sentence "what this does" — like the chat-style opener.</summary>
    public string Headline { get; init; } = "";
    /// <summary>Per-aspect bullet lines (source, filters, aggregation, sorting…).</summary>
    public List<string> Bullets { get; init; } = [];
    /// <summary>Deterministic safety/correctness warnings (DELETE without WHERE, …).</summary>
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Deterministic (non-AI) plain-English description of a SQL script. Parses the
/// text with a quote/bracket-aware scanner — no DB access, no ML — and produces
/// sentences like "Gets all rows from product basic where Id is greater than
/// 30003 and Count is greater than 5, sorted by name ascending."
/// Best-effort for common T-SQL: DML, SELECT clauses, simple predicates.
/// </summary>
public static class SqlExplainerService
{
    public static string Explain(string sql)
    {
        var cleaned = StripComments(sql);
        var statements = SplitStatements(cleaned);
        if (statements.Count == 0)
            return "The script is empty.";

        var parts = statements.Take(4).Select(ExplainStatement).ToList();
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0) sb.Append(" Then, it ");
            sb.Append(parts[i]);
        }
        if (statements.Count > 4)
            sb.Append($" (+{statements.Count - 4} more statement(s))");
        return Regex.Replace(sb.ToString(), @" {2,}", " ");
    }

    /// <summary>Chat-style breakdown: headline + bullets + safety warnings per statement.</summary>
    public static SqlExplanation ExplainDetailed(string sql)
    {
        var cleaned = StripComments(sql);
        var statements = SplitStatements(cleaned);
        var result = new SqlExplanation();
        if (statements.Count == 0)
        {
            return new SqlExplanation { Headline = "The script is empty — nothing to analyze yet." };
        }

        var bullets = new List<string>();
        var warnings = new List<string>();
        var headlines = new List<string>();

        foreach (var stmt in statements.Take(4))
        {
            var first = FirstWord(stmt).ToUpperInvariant();
            switch (first)
            {
                case "SELECT":
                    CollectSelectBreakdown(stmt, bullets, warnings);
                    headlines.Add(Regex.Replace(ExplainSelect(stmt), @" {2,}", " "));
                    break;
                case "DELETE":
                    bullets.Add("🗑 Deletes rows" + TargetOf(stmt, @"(?i)DELETE\s+FROM\s+([^\s]+)") +
                                (FirstTopLevel(stmt, "WHERE") == null
                                    ? " — no WHERE clause"
                                    : " matching the WHERE conditions"));
                    if (FirstTopLevel(stmt, "WHERE") == null)
                        warnings.Add("⚠ This DELETE has no WHERE clause — it will remove EVERY row in the table.");
                    break;
                case "UPDATE":
                    bullets.Add("✏️ Updates rows" + TargetOf(stmt, @"(?i)UPDATE\s+([^\s]+)") +
                                (FirstTopLevel(stmt, "WHERE") == null
                                    ? " — no WHERE clause"
                                    : " matching the WHERE conditions"));
                    if (FirstTopLevel(stmt, "WHERE") == null)
                        warnings.Add("⚠ This UPDATE has no WHERE clause — it will change EVERY row in the table.");
                    break;
                case "INSERT":
                    bullets.Add("➕ Inserts new rows into " +
                                (Regex.Match(stmt, @"(?i)INSERT\s+INTO\s+([^\s(]+)") is { Success: true } im
                                    ? HumanizeTable(im.Groups[1].Value) : "a table") + ".");
                    break;
                default:
                    bullets.Add($"• Runs a {first} statement.");
                    break;
            }
        }

        if (statements.Count > 4)
            bullets.Add($"…plus {statements.Count - 4} more statement(s).");

        var headline = headlines.Count > 0
            ? string.Join(" Then, it ", headlines)
            : Explain(sql);
        return new SqlExplanation { Headline = headline, Bullets = bullets, Warnings = warnings };
    }

    private static string TargetOf(string stmt, string pattern) =>
        Regex.Match(stmt, pattern) is { Success: true } m ? " from " + HumanizeTable(m.Groups[1].Value) : "";

    private static void CollectSelectBreakdown(string stmt, List<string> bullets, List<string> warnings)
    {
        var selectPart = SplitTopLevel(stmt, "FROM");
        var selectClause = selectPart[0];
        var rest = selectPart.Count > 1 ? selectPart[1] : "";

        var columnsText = selectClause.Length > 6 ? selectClause[6..].Trim() : "*";
        var distinct = false;
        int? top = null;
        if (Regex.Match(columnsText, @"^\s*DISTINCT\b", RegexOptions.IgnoreCase) is { Success: true } dm)
        { distinct = true; columnsText = columnsText[(dm.Length)..].Trim(); }
        if (Regex.Match(columnsText, @"^\s*TOP\s+(\d+)\b", RegexOptions.IgnoreCase) is { Success: true } tm)
        { top = int.Parse(tm.Groups[1].Value); columnsText = columnsText[(tm.Length)..].Trim(); }
        if (top != null && FirstTopLevel(rest, "ORDER BY") == null && FirstTopLevel(stmt, "ORDER BY") == null)
            warnings.Add("⚠ TOP without ORDER BY: which rows count as 'first' is not guaranteed — add ORDER BY for stable results.");

        // Source tables + joins.
        var tablesText = rest;
        string? wherePart = null, groupPart = null, havingPart = null, orderPart = null;
        var mWhere = FirstTopLevel(rest, "WHERE");
        if (mWhere != null) { tablesText = mWhere.Value.before; wherePart = mWhere.Value.after; }
        var mGroup = wherePart != null ? FirstTopLevel(wherePart, "GROUP BY") : null;
        if (mGroup != null) { wherePart = mGroup.Value.before; groupPart = mGroup.Value.after; }
        var mHaving = groupPart != null ? FirstTopLevel(groupPart, "HAVING") : null;
        if (mHaving != null) { groupPart = mHaving.Value.before; havingPart = mHaving.Value.after; }
        var mOrder = (havingPart ?? groupPart) != null
            ? FirstTopLevel(havingPart ?? groupPart!, "ORDER BY")
            : (wherePart != null ? FirstTopLevel(wherePart, "ORDER BY") : FirstTopLevel(tablesText, "ORDER BY"));
        if (mOrder != null)
        {
            if (havingPart != null) havingPart = mOrder.Value.before;
            else if (groupPart != null) groupPart = mOrder.Value.before;
            else if (wherePart != null) wherePart = mOrder.Value.before;
            else tablesText = mOrder.Value.before;
            orderPart = mOrder.Value.after;
        }

        var tableChunks = SplitTopLevel(tablesText, "JOIN");
        var primary = tableChunks[0].Trim();
        var primaryWords = primary.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sourceLine = "📖 Reads from " + (primaryWords.Length > 0 ? HumanizeTable(primaryWords[0]) : "?");
        if (tableChunks.Count > 1)
        {
            var joinTargets = new List<string>();
            for (var i = 1; i < tableChunks.Count; i++)
            {
                var words = tableChunks[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0)
                    joinTargets.Add(HumanizeTable(words[0]));
            }
            sourceLine += ", joined to " + string.Join(" and ", joinTargets);
        }
        bullets.Add(sourceLine + ".");

        if (columnsText.Trim().Contains('*') && !columnsText.Contains('('))
            bullets.Add("🧾 Returns all columns of the matched rows.");
        var aggLine = DescribeSelectList(columnsText).Trim();
        if (aggLine.StartsWith("—"))
            bullets.Add("🧮 Calculates " + aggLine[1..].Trim().TrimEnd(' ') + ".");

        if (!string.IsNullOrWhiteSpace(wherePart))
        {
            var conds = DescribePredicates(wherePart);
            if (conds.Length > 0)
                bullets.Add("🔎 Keeps only rows where " + conds + ".");
        }
        if (!string.IsNullOrWhiteSpace(groupPart))
        {
            var cols = SplitTopLevel(groupPart, ",").Select(HumanizeColumn).Where(c => c.Length > 0).ToList();
            if (cols.Count > 0)
                bullets.Add("🧩 Groups the rows by " + string.Join(", ", cols) +
                            " — one result row per unique combination.");
        }
        if (!string.IsNullOrWhiteSpace(havingPart))
        {
            var conds = DescribePredicates(havingPart, aggregateAware: true);
            if (conds.Length > 0)
                bullets.Add("🚮 Keeps only groups where " + conds + " (HAVING runs after grouping).");
        }
        if (!string.IsNullOrWhiteSpace(orderPart))
        {
            var items = SplitTopLevel(orderPart, ",");
            var parts = new List<string>();
            foreach (var item in items)
            {
                var w = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0) continue;
                var col = HumanizeColumn(w[0]);
                var dir = w.Length > 1 && w[^1].StartsWith("DESC", StringComparison.OrdinalIgnoreCase)
                    ? "descending" : "ascending";
                parts.Add($"{col} ({dir})");
            }
            if (parts.Count > 0)
                bullets.Add("↕️ Sorts the final result by " + string.Join(", ", parts) + ".");
        }
        if (top is { } n)
            bullets.Add($"📊 Returns just the first {n:N0} row{(n == 1 ? "" : "s")} of that result.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scanner primitives — track quotes, bracketed identifiers, parens depth
    // ──────────────────────────────────────────────────────────────────────

    private static string StripComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var inSingle = false;
        var inBlock = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            if (inBlock)
            {
                if (c == '*' && next == '/') { inBlock = false; i++; }
                else if (c == '\n') sb.Append('\n');
                continue;
            }
            if (inSingle)
            {
                sb.Append(c);
                if (c == '\'' && next == '\'') { sb.Append(next); i++; }
                else if (c == '\'') inSingle = false;
                continue;
            }
            if (c == '\'' && !inBlock) { inSingle = true; sb.Append(c); continue; }
            if (c == '-' && next == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                sb.Append('\n');
                continue;
            }
            if (c == '/' && next == '*') { inBlock = true; i++; continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static List<string> SplitStatements(string sql)
    {
        var list = new List<string>();
        var current = new StringBuilder();
        var inSingle = false;
        var depth = 0;
        foreach (var rawLine in sql.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();
            if (!inSingle && trimmed.StartsWith("GO", StringComparison.OrdinalIgnoreCase) &&
                (trimmed.Length == 2 || int.TryParse(trimmed[2..].Trim(), out _)))
            {
                Flush(list, current);
                continue;
            }
            foreach (var c in line)
            {
                if (inSingle)
                {
                    current.Append(c);
                    if (c == '\'') inSingle = false;
                    continue;
                }
                if (c == '\'') { inSingle = true; current.Append(c); continue; }
                if (c == '(') depth++;
                else if (c == ')') depth = Math.Max(0, depth - 1);
                else if (c == ';' && depth == 0) { Flush(list, current); continue; }
                current.Append(c);
            }
            current.Append('\n');
        }
        Flush(list, current);
        return list;
    }

    private static void Flush(List<string> list, StringBuilder current)
    {
        var text = current.ToString().Trim();
        current.Clear();
        if (!string.IsNullOrWhiteSpace(text))
            list.Add(text);
    }

    /// <summary>Splits on a top-level keyword; "GROUP BY" and "ORDER BY" two-word forms supported.</summary>
    private static List<string> SplitTopLevel(string text, string keyword)
    {
        var result = new List<string>();
        var inSingle = false;
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inSingle) { if (c == '\'') inSingle = false; continue; }
            if (c == '\'') { inSingle = true; continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
            if (depth == 0 && MatchesKeywordAt(text, i, keyword))
            {
                result.Add(text[start..i].Trim());
                i += keyword.Length - 1;
                start = i + 1;
            }
        }
        result.Add(text[start..].Trim());
        return result;
    }

    private static bool MatchesKeywordAt(string text, int pos, string keyword)
    {
        if (pos + keyword.Length > text.Length) return false;
        if (pos > 0 && IsWordChar(text[pos - 1])) return false;
        if (pos + keyword.Length < text.Length && IsWordChar(text[pos + keyword.Length])) return false;
        return string.Compare(text, pos, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string Clean(string raw) => raw.Trim().Trim('[', ']', '"');

    private static string HumanizeTable(string raw)
    {
        var name = Clean(raw);
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..];
        name = name.Trim('[', ']');
        name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");   // CamelCase → Camel Case
        name = name.Replace('_', ' ').Replace('-', ' ');
        return Regex.Replace(name, @"\s+", " ").Trim().ToLowerInvariant();
    }

    private static string HumanizeColumn(string raw)
    {
        var name = Clean(raw);
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..];
        name = name.Trim('[', ']');
        if (name.Contains('*')) return "all columns";
        name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
        name = name.Replace('_', ' ').Replace('-', ' ');
        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    private static string OpWords(string op) => op.Trim() switch
    {
        "="  => "equals",
        "<>" => "does not equal",
        "!=" => "does not equal",
        ">"  => "is greater than",
        ">=" => "is greater than or equal to",
        "<"  => "is less than",
        "<=" => "is less than or equal to",
        _    => op
    };

    // ──────────────────────────────────────────────────────────────────────
    // Per-statement explanation
    // ──────────────────────────────────────────────────────────────────────

    private static string ExplainStatement(string stmt)
    {
        var first = FirstWord(stmt);
        return first.ToUpperInvariant() switch
        {
            "SELECT" => ExplainSelect(stmt),
            "INSERT" => ExplainInsert(stmt),
            "UPDATE" => ExplainUpdate(stmt),
            "DELETE" => ExplainDelete(stmt),
            "EXEC" or "EXECUTE" => $"runs the stored procedure {FirstArgAfterKeyword(stmt, 1)}.",
            "DECLARE" => "declares local variables.",
            "SET" => "changes a session setting.",
            "USE" => $"switches to the {FirstArgAfterKeyword(stmt, 1)} database.",
            "CREATE" or "ALTER" or "DROP" => $"runs a {first.ToUpperInvariant()} statement on {FirstArgAfterKeyword(stmt, 1)}.",
            "IF" or "WHILE" => "runs conditional logic.",
            "BEGIN" => "runs a batch block.",
            _ => $"runs a {first.ToUpperInvariant()} statement."
        };
    }

    private static string FirstWord(string text)
    {
        var m = Regex.Match(text.TrimStart(), @"[A-Za-z_#@]+");
        return m.Success ? m.Value : "?";
    }

    private static string FirstArgAfterKeyword(string stmt, int skip)
    {
        var tokens = stmt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return skip < tokens.Length ? HumanizeTable(tokens[skip].TrimEnd(',', ';')) : "an object";
    }

    private static string ExplainSelect(string stmt)
    {
        var selectPart = SplitTopLevel(stmt, "FROM");
        var selectClause = selectPart[0];
        var rest = selectPart.Count > 1 ? selectPart[1] : "";

        // SELECT keyword itself is index 0 → columns list.
        var columnsText = selectClause.Length > 6 ? selectClause[6..].Trim() : "*";
        var distinct = false;
        int? top = null;
        if (Regex.Match(columnsText, @"^\s*DISTINCT\b", RegexOptions.IgnoreCase) is { Success: true } dm)
        { distinct = true; columnsText = columnsText[(dm.Length)..].Trim(); }
        if (Regex.Match(columnsText, @"^\s*TOP\s+(\d+)\b", RegexOptions.IgnoreCase) is { Success: true } tm)
        { top = int.Parse(tm.Groups[1].Value); columnsText = columnsText[(tm.Length)..].Trim(); }

        // FROM / JOINs / WHERE / GROUP BY / HAVING / ORDER BY / OFFSET — left to right.
        string? wherePart = null, groupPart = null, havingPart = null, orderPart = null, offsetPart = null;
        var tablesText = rest;
        var mWhere = FirstTopLevel(rest, "WHERE");
        if (mWhere != null) { tablesText = mWhere.Value.before; wherePart = mWhere.Value.after; }
        var mGroup = wherePart != null ? FirstTopLevel(wherePart, "GROUP BY") : null;
        if (mGroup != null) { wherePart = mGroup.Value.before; groupPart = mGroup.Value.after; }
        var mHaving = groupPart != null ? FirstTopLevel(groupPart, "HAVING") : null;
        if (mHaving != null) { groupPart = mHaving.Value.before; havingPart = mHaving.Value.after; }
        var mOrder = (havingPart ?? groupPart) != null
            ? FirstTopLevel(havingPart ?? groupPart!, "ORDER BY")
            : (wherePart != null ? FirstTopLevel(wherePart, "ORDER BY") : FirstTopLevel(tablesText, "ORDER BY"));
        if (mOrder != null)
        {
            if (havingPart != null) { havingPart = mOrder.Value.before; }
            else if (groupPart != null) { groupPart = mOrder.Value.before; }
            else if (wherePart != null) { wherePart = mOrder.Value.before; }
            else { tablesText = mOrder.Value.before; }
            orderPart = mOrder.Value.after;
        }
        var mOffset = orderPart != null ? FirstTopLevel(orderPart, "OFFSET") : null;
        if (mOffset != null) { orderPart = mOffset.Value.before; offsetPart = mOffset.Value.after; }

        var sb = new StringBuilder();
        if (top is { } n)
            sb.Append($"Gets the first {n:N0} ");
        else
            sb.Append("Gets all ");
        if (distinct) sb.Append("unique ");
        sb.Append("rows ");
        sb.Append(DescribeSelectList(columnsText));

        // FROM: primary table + JOINs.
        var tableChunks = SplitTopLevel(tablesText, "JOIN");
        var primary = tableChunks[0].Trim();
        var primaryWords = primary.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (primaryWords.Length > 0)
        {
            var tName = primaryWords[0];
            var alias = primaryWords.Length > 1 &&
                        !primaryWords[1].Equals("INNER", StringComparison.OrdinalIgnoreCase) &&
                        !primaryWords[1].Equals("LEFT", StringComparison.OrdinalIgnoreCase) &&
                        !primaryWords[1].Equals("RIGHT", StringComparison.OrdinalIgnoreCase) &&
                        !primaryWords[1].Equals("FULL", StringComparison.OrdinalIgnoreCase) &&
                        !primaryWords[1].Equals("CROSS", StringComparison.OrdinalIgnoreCase)
                ? $" (as {Clean(primaryWords[1])})"
                : "";
            sb.Append($" from {HumanizeTable(tName)}{alias}");
        }
        for (var i = 1; i < tableChunks.Count; i++)
        {
            var chunk = tableChunks[i];
            var joinKind = "joined to";
            // The keyword(s) before this JOIN were appended to the previous chunk; detect type by last word of previous chunk.
            var prev = i == 1 ? tablesText[..0] : tableChunks[i - 1];
            if (prev.EndsWith("LEFT", StringComparison.OrdinalIgnoreCase) ||
                chunk.StartsWith("OUTER", StringComparison.OrdinalIgnoreCase))
                joinKind = "left-joined to";
            else if (prev.EndsWith("RIGHT", StringComparison.OrdinalIgnoreCase))
                joinKind = "right-joined to";
            else if (prev.EndsWith("FULL", StringComparison.OrdinalIgnoreCase))
                joinKind = "fully joined to";
            else if (prev.EndsWith("CROSS", StringComparison.OrdinalIgnoreCase))
                joinKind = "cross-joined to";
            else if (prev.EndsWith("INNER", StringComparison.OrdinalIgnoreCase))
                joinKind = "joined to";

            var words = chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var table = words.Length > 0 ? HumanizeTable(words[0]) : "?";
            var onIdx = IndexOfTopLevelKeyword(chunk, "ON");
            var onText = onIdx >= 0 ? chunk[(onIdx + 4)..].Trim() : "";
            sb.Append($", {joinKind} {table}");
            if (!string.IsNullOrWhiteSpace(onText))
            {
                var sides = onText.Split('=', 2);
                if (sides.Length == 2)
                    sb.Append($" on {HumanizeColumn(sides[0])} = {HumanizeColumn(sides[1])}");
            }
        }

        if (!string.IsNullOrWhiteSpace(wherePart))
        {
            var predicates = DescribePredicates(wherePart);
            if (predicates.Length > 0)
                sb.Append($" where {predicates}");
        }
        if (!string.IsNullOrWhiteSpace(groupPart))
        {
            var cols = SplitTopLevel(groupPart, ",").Select(HumanizeColumn).Where(c => c.Length > 0);
            if (cols.Any())
                sb.Append($" grouped by {string.Join(", ", cols)}");
        }
        if (!string.IsNullOrWhiteSpace(havingPart))
        {
            var conds = DescribePredicates(havingPart, aggregateAware: true);
            if (conds.Length > 0)
                sb.Append($" keeping only groups where {conds}");
        }
        if (!string.IsNullOrWhiteSpace(orderPart))
        {
            var items = SplitTopLevel(orderPart, ",");
            var parts = new List<string>();
            foreach (var item in items)
            {
                var w = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0) continue;
                var col = HumanizeColumn(w[0]);
                var dir = w.Length > 1 && w[^1].StartsWith("DESC", StringComparison.OrdinalIgnoreCase)
                    ? "descending" : "ascending";
                parts.Add($"{col} ({dir})");
            }
            if (parts.Count > 0)
                sb.Append($" sorted by {string.Join(", ", parts)}");
        }
        if (!string.IsNullOrWhiteSpace(offsetPart))
        {
            // offsetPart is the text after the OFFSET keyword.
            var m = Regex.Match(offsetPart, @"^\s*(\d+)\s+ROWS(?:\s+FETCH\s+NEXT\s+(\d+)\s+ROWS\s+ONLY)?",
                RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var skip = int.Parse(m.Groups[1].Value);
                var take = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : (int?)null;
                sb.Append(take is { } t
                    ? $", skipping {skip:N0} rows and returning the next {t:N0}"
                    : $", skipping {skip:N0} rows");
            }
        }
        sb.Append('.');
        return sb.ToString();
    }

    private static string DescribeSelectList(string columnsText)
    {
        var items = SplitTopLevel(columnsText, ",");
        var aggregates = new List<string>();
        var plain = 0;
        foreach (var item in items)
        {
            var am = Regex.Match(item, @"(?i)\b(COUNT_BIG|COUNT|SUM|AVG|MIN|MAX|STDEV|STDEVP|VAR|VARP|STRING_AGG)\s*\(([^()]*)\)");
            if (am.Success)
            {
                var fn = am.Groups[1].Value.ToUpperInvariant();
                var arg = am.Groups[2].Value.Trim();
                var target = fn == "COUNT" && arg == "*" ? "rows" : HumanizeColumn(arg);
                var verb = fn switch
                {
                    "COUNT" or "COUNT_BIG" => $"count of {target}",
                    "SUM" => $"total of {target}",
                    "AVG" => $"average {target}",
                    "MIN" => $"smallest {target}",
                    "MAX" => $"largest {target}",
                    "STDEV" or "STDEVP" => $"spread of {target}",
                    "VAR" or "VARP" => $"variance of {target}",
                    "STRING_AGG" => $"joined text of {target}",
                    _ => fn
                };
                aggregates.Add(verb);
            }
            else plain++;
        }
        if (aggregates.Count > 0)
            return $"— calculating {string.Join(", ", aggregates)}" +
                   (plain > 0 ? $" (plus {plain} regular column{(plain == 1 ? "" : "s")})" : "") + " ";
        if (items.Count == 1 && items[0].Trim().Contains('*'))
            return "";
        return $"— selecting {items.Count} column{(items.Count == 1 ? "" : "s")} ";
    }

    private static (string before, string after)? FirstTopLevel(string text, string keyword)
    {
        var inSingle = false;
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inSingle) { if (c == '\'') inSingle = false; continue; }
            if (c == '\'') { inSingle = true; continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
            if (depth == 0 && MatchesKeywordAt(text, i, keyword))
                return (text[..i].Trim(), text[(i + keyword.Length)..].Trim());
        }
        return null;
    }

    private static int IndexOfTopLevelKeyword(string text, string keyword)
    {
        var inSingle = false;
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inSingle) { if (c == '\'') inSingle = false; continue; }
            if (c == '\'') { inSingle = true; continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
            if (depth == 0 && MatchesKeywordAt(text, i, keyword))
                return i;
        }
        return -1;
    }

    /// <summary>Renders "a is greater than 1 and b equals 2" from a WHERE/HAVING clause body.
    /// An AND directly following BETWEEN belongs to the range and does not split.</summary>
    private static string DescribePredicates(string clause, bool aggregateAware = false)
    {
        var inSingle = false;
        var depth = 0;
        var pieces = new List<string>();
        var start = 0;
        var expectBetweenAnd = false;
        for (var i = 0; i < clause.Length; i++)
        {
            var c = clause[i];
            if (c == '\'') inSingle = !inSingle;
            if (inSingle) continue;
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
            if (depth != 0) continue;
            if (MatchesKeywordAt(clause, i, "BETWEEN"))
            {
                expectBetweenAnd = true;
                continue;
            }
            var isAnd = MatchesKeywordAt(clause, i, "AND");
            var isOr = !isAnd && MatchesKeywordAt(clause, i, "OR");
            if (!isAnd && !isOr) continue;
            if (isAnd && expectBetweenAnd)
            {
                // The range's own AND — keep it inside the predicate.
                expectBetweenAnd = false;
                continue;
            }
            pieces.Add(clause[start..i].Trim());
            pieces.Add(isOr ? "or" : "and");
            i += 2;
            start = i + 1;
        }
        pieces.Add(clause[start..].Trim());

        var sb = new StringBuilder();
        var first = true;
        foreach (var piece in pieces)
        {
            var rendered = piece.Equals("and", StringComparison.OrdinalIgnoreCase) ||
                           piece.Equals("or", StringComparison.OrdinalIgnoreCase)
                ? piece.ToLowerInvariant()
                : DescribeOnePredicate(piece, aggregateAware);
            if (rendered.Length == 0) continue;
            if (!first) sb.Append(' ');
            sb.Append(rendered);
            first = false;
        }
        return sb.ToString();
    }

    private static string DescribeOnePredicate(string p, bool aggregateAware)
    {
        p = p.Trim();
        if (p.Length == 0) return "";

        var m = Regex.Match(p, @"^(?<lhs>.+?)\s+(?<op>IS\s+NOT\s+NULL|IS\s+NULL|NOT\s+LIKE|LIKE|NOT\s+IN|IN|BETWEEN|<>|!=|>=|<=|=|>|<)\s*(?<rhs>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            return $"a condition on {HumanizeColumn(p)}";

        var lhs = m.Groups["lhs"].Value.Trim();
        var op = Regex.Replace(m.Groups["op"].Value, @"\s+", " ").ToUpperInvariant();
        var rhs = m.Groups["rhs"].Value.Trim();

        var lhsText = HumanizeColumn(lhs);
        if (aggregateAware)
        {
            var am = Regex.Match(lhs, @"(?i)^\s*(COUNT_BIG|COUNT|SUM|AVG|MIN|MAX|STDEV|STDEVP|VAR|VARP)\s*\(\s*([^()]+?)\s*\)\s*$");
            if (am.Success)
            {
                var fn = am.Groups[1].Value.ToUpperInvariant();
                var arg = am.Groups[2].Value;
                lhsText = fn == "COUNT" && arg == "*" ? "row count" : $"{fn} of {HumanizeColumn(arg)}";
            }
        }

        return op switch
        {
            "IS NULL"      => $"{lhsText} is empty (NULL)",
            "IS NOT NULL"  => $"{lhsText} has a value (not NULL)",
            "LIKE"         => $"{lhsText} matches pattern {StripQuotes(rhs)}",
            "NOT LIKE"     => $"{lhsText} does not match pattern {StripQuotes(rhs)}",
            "IN"           => $"{lhsText} is one of {rhs}",
            "NOT IN"       => $"{lhsText} is not one of {rhs}",
            "BETWEEN"      => DescribeBetween(lhsText, rhs),
            _              => $"{lhsText} {OpWords(op)} {StripQuotes(rhs)}"
        };
    }

    private static string DescribeBetween(string lhs, string rhs)
    {
        var andIdx = IndexOfTopLevelKeyword(rhs, "AND");
        if (andIdx < 0) return $"{lhs} is in range {StripQuotes(rhs)}";
        var lo = StripQuotes(rhs[..andIdx].Trim());
        var hi = StripQuotes(rhs[(andIdx + 3)..].Trim());
        return $"{lhs} is between {lo} and {hi}";
    }

    private static string StripQuotes(string raw)
    {
        raw = raw.Trim();
        if (raw.Length >= 2 && raw.StartsWith('\'') && raw.EndsWith('\''))
            return raw[1..^1];
        return raw;
    }

    private static string ExplainInsert(string stmt)
    {
        var m = Regex.Match(stmt, @"(?i)INSERT\s+INTO\s+([^\s(]+)");
        var table = m.Success ? HumanizeTable(m.Groups[1].Value) : "a table";
        return $"adds new row(s) into {table}.";
    }

    private static string ExplainUpdate(string stmt)
    {
        var m = Regex.Match(stmt, @"(?i)UPDATE\s+([^\s]+)");
        var table = m.Success ? HumanizeTable(m.Groups[1].Value) : "a table";
        var setPart = FirstTopLevel(stmt, "SET");
        var rest = setPart != null ? setPart.Value.after : "";
        var sb = new StringBuilder($"changes existing row(s) in {table}");
        var where = FirstTopLevel(rest, "WHERE");
        if (where != null && !string.IsNullOrWhiteSpace(where.Value.before))
            sb.Append($" — setting {where.Value.before.Replace("=", " to ")}");
        if (where != null)
        {
            var conds = DescribePredicates(where.Value.after);
            if (conds.Length > 0) sb.Append($" where {conds}");
        }
        sb.Append('.');
        return sb.ToString();
    }

    private static string ExplainDelete(string stmt)
    {
        var m = Regex.Match(stmt, @"(?i)DELETE\s+FROM\s+([^\s]+)");
        var table = m.Success ? HumanizeTable(m.Groups[1].Value) : "a table";
        var where = FirstTopLevel(stmt, "WHERE");
        var sb = new StringBuilder($"deletes row(s) from {table}");
        if (where != null)
        {
            var conds = DescribePredicates(where.Value.after);
            if (conds.Length > 0) sb.Append($" where {conds}");
        }
        sb.Append('.');
        return sb.ToString();
    }
}
