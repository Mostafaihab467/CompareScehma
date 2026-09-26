using System.Text;
using System.Text.RegularExpressions;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// The guard that runs a script with your eyes open: what this text will destroy, read off the
/// text itself before the server sees it.
///
/// SSMS's own protection is "are you sure", which an operator answers by reflex. This names the
/// statement and the damage — a <c>DELETE</c> with no <c>WHERE</c> removes every row of the table
/// it names, and a <c>WHERE 1=1</c> filters nothing — so the confirmation carries the information
/// the reflex was swallowing. It reads only the script: it never connects, never counts rows and
/// never decides what is safe for the database, because only the operator knows whether that table
/// is empty anyway.
///
/// Everything runs over <see cref="SqlLintService.StripStringsAndComments"/>, so a DELETE in a
/// comment cannot start a warning — the false alarms are what make operators switch a guard off,
/// and then the real ones arrive unannounced. The same mask is why a write hidden inside a
/// dynamic-SQL string is invisible here; that one is accepted, not missed.
/// </summary>
public static class QueryGuardService
{
    // A verb that can begin a statement. T-SQL does not require the semicolon that would make
    // finding the end of one trivial, so the next verb at bracket depth zero is the boundary.
    // SET is deliberately absent: it is the middle of an UPDATE, and cutting there would make
    // every filtered <c>UPDATE … SET … WHERE …</c> look unfiltered.
    private static readonly string[] StatementVerbs =
    [
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "EXEC", "EXECUTE",
        "CREATE", "ALTER", "DROP", "TRUNCATE", "BEGIN", "COMMIT", "ROLLBACK",
        "IF", "WHILE", "DECLARE", "USE", "PRINT", "GRANT", "REVOKE", "GO",
    ];

    private const string NamePattern =
        @"\[?[A-Za-z_#@$][\w#@$.\[\]]*\]?(?:\s*\.\s*\[?[A-Za-z_#@$][\w#@$.\[\]]*\]?)*";

    private static readonly Regex DeleteRegex = Rx(
        $@"\bDELETE(?!\w)(?:\s+(?:FROM\s+)?(?<t>{NamePattern}))?");
    private static readonly Regex UpdateRegex = Rx($@"\bUPDATE(?!\w)\s+(?<t>{NamePattern})");
    private static readonly Regex TruncateRegex = Rx($@"\bTRUNCATE\s+TABLE\s+(?<t>{NamePattern})");
    private static readonly Regex DropRegex = Rx(
        $@"\bDROP\s+(?<t>TABLE|DATABASE|VIEW|PROCEDURE|PROC|FUNCTION|SCHEMA|SEQUENCE|SYNONYM|INDEX)\s+(?<n>{NamePattern})");
    // The dropped thing has to be a column: ALTER TABLE … DROP CONSTRAINT/INDEX/PRIMARY KEY
    // removes a rule, not the data in a column of every row.
    private static readonly Regex DropColumnRegex = Rx(
        $@"\bALTER\s+TABLE\s+(?<t>{NamePattern})\s+DROP\s+(?:COLUMN\s+)?"
        + @"(?!CONSTRAINT\b|INDEX\b|PRIMARY\b|UNIQUE\b|FOREIGN\b|CHECK\b|PROCEDURE\b)(?<c>[A-Za-z_]\w*)");
    private static readonly Regex WhereRegex = Rx(@"\bWHERE(?!\w)");

    // A WHERE made only of tautologies. Multiline so the end of the script counts as an end.
    private static readonly Regex UselessWhereRegex = new(
        @"\bWHERE\s+1\s*=\s*1\s*(AND\s+1\s*=\s*1\s*)*(?=;|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
        | RegexOptions.Multiline);

    private static Regex Rx(string pattern) => new(
        pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static GuardReport Analyze(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return new GuardReport([]);
        var masked = SqlLintService.StripStringsAndComments(sql);
        var statements = Statements(masked);
        var findings = new List<GuardFinding>();

        foreach (Match m in DeleteRegex.Matches(masked))
            AddFilterFinding(findings, "delete-without-where", "DELETE FROM",
                "removes every row of the table", masked, statements, m.Index, m.Groups["t"].Value);

        foreach (Match m in UpdateRegex.Matches(masked))
        {
            // UPDATE STATISTICS is a maintenance command, not a rewrite of the table.
            if (string.Equals(m.Groups["t"].Value.Trim(), "STATISTICS", StringComparison.OrdinalIgnoreCase))
                continue;
            AddFilterFinding(findings, "update-without-where", "UPDATE",
                "rewrites every row of the table", masked, statements, m.Index, m.Groups["t"].Value);
        }

        foreach (Match m in TruncateRegex.Matches(masked))
            findings.Add(Dangerous("truncate-table",
                $"{Phrase("TRUNCATE TABLE", m.Groups["t"].Value)} empties the table in one pass and resets its identity — no WHERE can narrow it."));

        foreach (Match m in DropColumnRegex.Matches(masked))
            findings.Add(Dangerous("alter-drop-column",
                $"ALTER TABLE {Name(m.Groups["t"].Value)} DROP COLUMN {m.Groups["c"].Value} discards that column's data in every row."));

        foreach (Match m in DropRegex.Matches(masked))
        {
            var kind = m.Groups["t"].Value.Trim().ToUpperInvariant();
            var statement = Phrase("DROP " + kind, m.Groups["n"].Value);
            findings.Add(kind switch
            {
                "TABLE" => Dangerous("drop-table",
                    $"{statement} removes the table and every row in it, not just the definition."),
                "DATABASE" => Dangerous("drop-database",
                    $"{statement} takes the whole database away from everyone using it."),
                "INDEX" => Advisory("drop-index",
                    $"{statement} is rebuildable, but everything it served gets slower until it is."),
                _ => Advisory($"drop-{kind.ToLowerInvariant()}",
                    $"{statement} removes the object, and nothing here can put it back."),
            });
        }

        return new GuardReport(Order(findings));
    }

    /// <summary>
    /// Flags a write whose filter does not narrow anything. The filter may sit at the top level of
    /// the statement or in the CTE the statement deletes through — a <c>DELETE FROM c</c> removes
    /// exactly the rows <c>c</c> selected — but never inside a sub-query, whose WHERE constrains
    /// that sub-query and not the table being written.
    /// </summary>
    private static void AddFilterFinding(
        List<GuardFinding> findings, string rule, string verb, string damage,
        string masked, List<(int Start, int End)> statements, int at, string rawTarget)
    {
        var statement = Phrase(verb, rawTarget);
        var filter = FilterOf(StatementOf(statements, masked, at), rawTarget, masked);
        if (filter is null)
            findings.Add(Dangerous(rule, $"{statement} has no WHERE, so it {damage}."));
        else if (UselessWhereRegex.IsMatch(filter))
            findings.Add(Dangerous("where-filters-nothing",
                $"The WHERE on {statement} is only tautologies, so it {damage}."));
    }

    /// <summary>The text of the filter that decides how many rows the write touches, or null when
    /// the statement has none.</summary>
    private static string? FilterOf(string statementText, string rawTarget, string masked)
    {
        var outer = WithoutParenthesisedGroups(statementText);
        if (WhereRegex.IsMatch(outer)) return outer;
        return CteFilter(masked, rawTarget);
    }

    /// <summary>When the write targets a CTE defined in this script, that CTE's own predicate is
    /// the filter — <c>;WITH c AS (SELECT … WHERE …) DELETE FROM c</c> deletes those rows, not the
    /// table's.</summary>
    private static string? CteFilter(string masked, string rawTarget)
    {
        var target = ShortName(rawTarget);
        if (target.Length == 0) return null;
        foreach (Match m in Rx($@"\b{Regex.Escape(target)}\s+AS\s*\(").Matches(masked))
        {
            var body = BodyAt(masked, m.Index + m.Length);
            if (WhereRegex.IsMatch(body)) return body;
        }
        return null;
    }

    /// <summary>"UPDATE dbo.Orders", or the bare verb when the statement names no table.</summary>
    private static string Phrase(string verb, string raw)
    {
        var name = Name(raw);
        return name.Length == 0 ? verb : $"{verb} {name}";
    }

    /// <summary>
    /// The script cut into statements. T-SQL does not require the semicolon that would make this
    /// trivial, so a statement also ends where the next top-level verb begins:
    /// <c>IF EXISTS (…) DELETE FROM t</c> is one statement, <c>SELECT 1 DELETE FROM t</c> is two.
    /// </summary>
    private static List<(int Start, int End)> Statements(string masked)
    {
        var spans = new List<(int Start, int End)>();
        var depth = 0;
        var start = SkipSpace(masked, 0);
        var verbSeen = false;
        for (var i = start; i < masked.Length; i++)
        {
            var c = masked[i];
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
            if (depth > 0) continue;

            if (c == ';')
            {
                spans.Add((start, i + 1));
                start = SkipSpace(masked, i + 1);
                verbSeen = false;
                i = start - 1;
                continue;
            }
            if (!char.IsLetter(c) || IsWordChar(masked, i - 1)) continue;

            var end = i;
            while (end < masked.Length && IsWordChar(masked, end)) end++;
            var isVerb = Array.IndexOf(StatementVerbs, masked.Substring(i, end - i).ToUpperInvariant()) >= 0;
            if (isVerb && verbSeen)
            {
                spans.Add((start, i));
                start = i;
                i = end - 1;    // the verb that opens the new statement
            }
            else
            {
                if (isVerb) verbSeen = true;
                i = end - 1;
            }
        }
        if (SkipSpace(masked, start) < masked.Length) spans.Add((start, masked.Length));
        return spans;
    }

    private static string StatementOf(List<(int Start, int End)> spans, string masked, int index)
    {
        foreach (var (s, e) in spans)
            if (index >= s && index < e) return Slice(masked, s, e);
        return "";
    }

    /// <summary>The balanced text after an opening bracket, brackets nested inside it kept.</summary>
    private static string BodyAt(string masked, int from)
    {
        var depth = 0;
        for (var i = from; i < masked.Length; i++)
        {
            if (masked[i] == '(') depth++;
            else if (masked[i] == ')')
            {
                if (depth == 0) return Slice(masked, from, i);
                depth--;
            }
        }
        return Slice(masked, from, masked.Length);
    }

    private static bool IsWordChar(string text, int index) =>
        index >= 0 && index < text.Length
        && (char.IsLetterOrDigit(text[index]) || text[index] == '_');

    private static int SkipSpace(string text, int from)
    {
        var i = from;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i;
    }

    /// <summary>The last part of a possibly qualified name, without its brackets.</summary>
    private static string ShortName(string raw)
    {
        var name = Name(raw);
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    /// <summary>Drops everything inside parentheses: a sub-query's WHERE belongs to the sub-query,
    /// so <c>UPDATE t SET c = (SELECT … WHERE …)</c> still rewrites every row of t.</summary>
    private static string WithoutParenthesisedGroups(string text)
    {
        var depth = 0;
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (depth == 0) builder.Append(c);
        }
        return builder.ToString();
    }

    private static string Slice(string text, int from, int to) =>
        text.Substring(from, Math.Max(0, Math.Min(to, text.Length) - from));

    private static GuardFinding Dangerous(string rule, string message) => new(GuardLevel.Dangerous, rule, message);

    private static GuardFinding Advisory(string rule, string message) => new(GuardLevel.Advisory, rule, message);

    private static string Name(string raw) =>
        raw.Trim().TrimEnd(';', ' ', ',').Replace("[", "").Replace("]", "");

    private static List<GuardFinding> Order(List<GuardFinding> findings) =>
        findings.OrderByDescending(f => f.Level == GuardLevel.Dangerous)
            .ThenBy(f => f.Rule, StringComparer.Ordinal)
            .ToList();
}
