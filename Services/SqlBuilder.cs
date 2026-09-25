using System.Text;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Pure functional T-SQL generator for the visual Query Constructor. No I/O, no DB
/// calls — just turns a <see cref="QueryBuilderModel"/> into a formatted statement.
/// Identifiers are bracket-quoted; string literals are escaped by doubling single
/// quotes. Output is intentionally verbose (one clause per line) so users can
/// read and learn from the preview pane.
/// </summary>
public static class SqlBuilder
{
    /// <summary>Returns a formatted SELECT statement, or a "--" comment when invalid.</summary>
    public static string Build(QueryBuilderModel b)
    {
        if (b.Tables.Count == 0)
            return "-- Pick at least one table to begin";

        var sb = new StringBuilder();

        // ── SELECT clause ──────────────────────────────────────────────────────
        sb.Append("SELECT ");
        if (b.Distinct) sb.Append("DISTINCT ");
        if (b.TopN > 0) sb.Append("TOP ").Append(b.TopN).Append(' ');

        var selectItems = new List<string>();
        foreach (var t in b.Tables)
            foreach (var c in t.SelectedColumns)
                selectItems.Add(FormatSelectColumn(c));

        if (selectItems.Count == 0)
            sb.Append("*");
        else
            sb.AppendLine(string.Join($",{Environment.NewLine}       ", selectItems));
        sb.AppendLine();

        // ── FROM clause ────────────────────────────────────────────────────────
        var primary = b.Tables[0];
        sb.Append("FROM ").Append(primary.Qualified).Append(' ')
          .Append(primary.AliasOrName).AppendLine();

        // ── JOINs ──────────────────────────────────────────────────────────────
        foreach (var j in b.Joins)
        {
            var right = j.RightTable;
            if (string.IsNullOrWhiteSpace(right.Name))
                continue; // user added an empty join row, skip

            sb.Append(FormatJoinKeyword(j.JoinType)).Append(' ')
              .Append(right.Qualified).Append(' ')
              .Append(right.AliasOrName);

            if (j.JoinType == JoinType.Cross)
            {
                sb.AppendLine();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(j.LeftColumn) && !string.IsNullOrWhiteSpace(j.RightColumn))
                sb.Append(" ON ").Append(QuoteQualified(j.LeftColumn))
                  .Append(" = ").AppendLine(QuoteQualified(j.RightColumn));
            else
                sb.AppendLine(" ON /* TODO: pick ON columns */ 1 = 1");
        }
        sb.AppendLine();

        // ── WHERE ──────────────────────────────────────────────────────────────
        var filters = b.Filters.Where(f => !string.IsNullOrWhiteSpace(f.ColumnName)).ToList();
        if (filters.Count > 0)
        {
            sb.Append("WHERE ");
            for (var i = 0; i < filters.Count; i++)
            {
                var f = filters[i];
                if (i > 0) sb.Append(i == 0 ? "" : $" {f.Conjunction.ToString().ToUpperInvariant()} ");
                sb.Append(FormatFilter(f));
            }
            sb.AppendLine();
        }

        // ── GROUP BY ───────────────────────────────────────────────────────────
        var groups = b.GroupBy.Where(g => !string.IsNullOrWhiteSpace(g.ColumnName)).ToList();
        if (groups.Count > 0)
        {
            sb.Append("GROUP BY ").AppendLine(string.Join(", ",
                groups.Select(g => QuoteQualified($"{g.TableAlias}.{g.ColumnName}"))));
        }

        // ── HAVING (filters over aggregated groups) ───────────────────────────
        var having = b.Having
            .Where(h => h.Aggregate == AggregateFunction.CountStar || !string.IsNullOrWhiteSpace(h.ColumnName))
            .ToList();
        if (having.Count > 0)
        {
            sb.Append("HAVING ");
            for (var i = 0; i < having.Count; i++)
            {
                if (i > 0)
                    sb.Append($" {having[i].Conjunction.ToString().ToUpperInvariant()} ");
                sb.Append(FormatHaving(having[i]));
            }
            sb.AppendLine();
        }

        // ── ORDER BY ───────────────────────────────────────────────────────────
        if (b.OrderBy.Count > 0)
        {
            sb.Append("ORDER BY ")
              .AppendLine(string.Join(", ",
                  b.OrderBy.Select(o =>
                      $"{QuoteQualified($"{o.TableAlias}.{o.ColumnName}")} {o.Direction.ToString().ToUpperInvariant()}")));
        }

        // ── OFFSET / FETCH ─────────────────────────────────────────────────────
        if (b.FetchNext > 0 || b.OffsetRows > 0)
        {
            sb.Append("OFFSET ").Append(b.OffsetRows).Append(" ROWS").AppendLine();
            if (b.FetchNext > 0)
                sb.Append("FETCH NEXT ").Append(b.FetchNext).AppendLine(" ROWS ONLY");
        }

        // Trim the trailing blank line for a cleaner preview.
        var result = sb.ToString().TrimEnd();
        return result.Length == 0 ? "-- Empty builder" : result;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Formatting helpers
    // ──────────────────────────────────────────────────────────────────────

    private static string FormatSelectColumn(SelectedColumn c)
    {
        var inner = c.Aggregate switch
        {
            AggregateFunction.None      => $"{QuoteId(c.TableAlias)}.{QuoteId(c.ColumnName)}",
            AggregateFunction.CountStar => "COUNT(*)",
            _                          => $"{c.Aggregate.ToSql()}({QuoteId(c.TableAlias)}.{QuoteId(c.ColumnName)})"
        };
        return string.IsNullOrWhiteSpace(c.Alias) ? inner : $"{inner} AS {QuoteId(c.Alias)}";
    }

    private static string FormatJoinKeyword(JoinType t) => t switch
    {
        JoinType.Inner      => "INNER JOIN",
        JoinType.LeftOuter  => "LEFT OUTER JOIN",
        JoinType.RightOuter => "RIGHT OUTER JOIN",
        JoinType.FullOuter  => "FULL OUTER JOIN",
        JoinType.Cross      => "CROSS JOIN",
        _                   => "INNER JOIN"
    };

    private static string FormatFilter(FilterCondition f)
    {
        var left = $"{QuoteId(f.TableAlias)}.{QuoteId(f.ColumnName)}";
        return f.Operator switch
        {
            FilterOperator.IsNull      => $"{left} IS NULL",
            FilterOperator.IsNotNull   => $"{left} IS NOT NULL",
            FilterOperator.In          => $"{left} IN ({SplitList(f.Value)})",
            FilterOperator.NotIn       => $"{left} NOT IN ({SplitList(f.Value)})",
            FilterOperator.Between     => FormatBetween(left, f.Value),
            _                          => $"{left} {SqlOp(f.Operator)} {FormatScalar(f.Value)}"
        };
    }

    private static string FormatBetween(string left, string raw)
    {
        var parts = (raw ?? string.Empty).Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length == 2)
            return $"{left} BETWEEN {FormatScalar(parts[0])} AND {FormatScalar(parts[1])}";
        return $"{left} BETWEEN {FormatScalar(raw ?? string.Empty)} /* and */";
    }

    private static string FormatScalar(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "NULL";
        raw = raw ?? string.Empty;
        // Looks numeric?
        if (long.TryParse(raw, out _) || double.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out _))
            return raw;
        return $"'{raw.Replace("'", "''")}'";
    }

    private static string SplitList(string raw)
    {
        var parts = (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "''";
        return string.Join(", ", parts.Select(FormatScalar));
    }

    private static string SqlOp(FilterOperator op) => op switch
    {
        FilterOperator.Equal         => "=",
        FilterOperator.NotEqual      => "<>",
        FilterOperator.LessThan      => "<",
        FilterOperator.LessOrEqual   => "<=",
        FilterOperator.GreaterThan   => ">",
        FilterOperator.GreaterOrEqual=> ">=",
        FilterOperator.Like          => "LIKE",
        FilterOperator.NotLike       => "NOT LIKE",
        _                            => "="
    };

    private static string FormatHaving(HavingCondition h)
    {
        var left = h.Aggregate == AggregateFunction.CountStar
            ? "COUNT(*)"
            : $"{h.Aggregate.ToSql()}({QuoteId(h.TableAlias)}.{QuoteId(h.ColumnName)})";
        return h.Operator switch
        {
            FilterOperator.IsNull      => $"{left} IS NULL",
            FilterOperator.IsNotNull   => $"{left} IS NOT NULL",
            FilterOperator.In          => $"{left} IN ({SplitList(h.Value)})",
            FilterOperator.NotIn       => $"{left} NOT IN ({SplitList(h.Value)})",
            FilterOperator.Between     => FormatBetween(left, h.Value),
            _                          => $"{left} {SqlOp(h.Operator)} {FormatScalar(h.Value)}"
        };
    }

    /// <summary>Brackets each dotted segment of a "alias.column" identifier.</summary>
    private static string QuoteQualified(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "[]";
        var parts = raw.Split('.', 2);
        return parts.Length == 1 ? QuoteId(parts[0]) : $"{QuoteId(parts[0])}.{QuoteId(parts[1])}";
    }

    private static string QuoteId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "[]";
        var trimmed = id.Trim().Trim('[', ']');
        return $"[{trimmed.Replace("]", "]]")}]";
    }
}