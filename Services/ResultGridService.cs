using System.Globalization;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// The reading end of the result grid: what a column actually holds, which rows a set of filters
/// keeps, and what the same rows look like grouped by one column.
/// </summary>
/// <remarks>
/// Pure — it never connects, never runs SQL and never touches a control. Every rule below is
/// therefore assertable from a synthetic table, which is how it was written.
/// <para>
/// The one thing it promises: <see cref="Display"/> prints a cell the way the grid does, so the
/// value named in a filter list, the value in a pivot row and the value the operator just looked
/// at are the same string. A filter that matched on a different rendering than the one it shows
/// would be unfalsifiable from the screen.
/// </para>
/// </remarks>
public static class ResultGridService
{
    /// <summary>What an absent cell looks like in the grid, in the filter list and in a pivot row.</summary>
    public const string NullText = "(NULL)";

    /// <summary>
    /// The cell as the grid renders it. <see cref="DBNull.Value"/> and null share the grid's own
    /// placeholder, so "(NULL)" is tickable like any other value — which is the only honest way to
    /// ask "show me the rows with nothing here" without inventing a syntax a real text value could
    /// collide with.
    /// </summary>
    public static string Display(object? value) => value switch
    {
        null or DBNull => NullText,
        string s => s,
        bool b => b ? "True" : "False",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture) ?? "",
        _ => value.ToString() ?? ""
    };

    public static object? ValueOf(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is not DBNull ? value : null;

    /// <summary>
    /// Every distinct value one column holds, with its row count — most frequent first, then by
    /// text so two runs over the same result list the same way.
    /// </summary>
    /// <exception cref="ArgumentException">The column is not in this result set.</exception>
    public static IReadOnlyList<ResultValue> DistinctValues(
        QueryResultTable table, string column, IEnumerable<ResultFilter>? filters = null)
    {
        var known = RequireColumn(table, column);

        // The list is built over the rows the grid is showing, so a value that just stopped
        // matching cannot stay ticked in a list nobody can scroll back to.
        var rows = VisibleRows(table, filters);

        return rows
            .GroupBy(r => Display(ValueOf(r, known)), StringComparer.Ordinal)
            .Select(g => new ResultValue(g.Key, g.Count(), g.Key == NullText))
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The rows the filters keep, in the order the server sent them.
    /// </summary>
    /// <remarks>
    /// A row survives when <em>every</em> active filter accepts it. Within one column the ticked
    /// values OR together and the free text must also be contained — a column that asks for both
    /// is asking for the intersection, which is what "these three values, and only those with 42
    /// in them" means. An inactive filter constrains nothing, and no filters at all keep every
    /// row, so the unfiltered path is this method too rather than a second code path.
    /// </remarks>
    public static List<Dictionary<string, object?>> VisibleRows(
        QueryResultTable table, IEnumerable<ResultFilter>? filters)
    {
        var active = (filters ?? []).Where(f => f.IsActive).ToList();
        if (active.Count == 0) return table.Rows.ToList();

        return table.Rows.Where(row => active.All(f => Matches(row, f))).ToList();
    }

    /// <summary>Whether one row passes one column's filter.</summary>
    public static bool Matches(IReadOnlyDictionary<string, object?> row, ResultFilter filter)
    {
        if (!filter.IsActive) return true;

        // A missing key is a column the result never had, not an empty cell: it matches nothing,
        // because claiming otherwise would make a typo'd filter silently keep every row.
        if (!row.ContainsKey(filter.Column)) return false;

        var text = Display(ValueOf(row, filter.Column));
        var isNull = text == NullText;

        if (filter.Values.Count > 0 && !filter.Values.Contains(text)) return false;

        // NULL is a fact about the cell, not a string, so free text never matches it. Ticking
        // "(NULL)" in the value list is how a row with nothing in it gets asked for.
        if (isNull) return filter.Values.Count > 0;

        if (!string.IsNullOrWhiteSpace(filter.Text) &&
            !text.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Group the rows being shown by one column and reduce each group to a single number.
    /// </summary>
    /// <remarks>
    /// The grouping is over <see cref="VisibleRows"/>, never the whole result: a pivot that
    /// quietly ignores the filters the operator is looking at answers a question nobody asked.
    /// <para>
    /// <c>COUNT</c> counts rows and needs no value column. <c>SUM</c>/<c>AVERAGE</c> need numbers
    /// and refuse a column that holds anything else rather than treating text as zero. Every
    /// aggregate skips empty cells, so an average is the mean of the values that exist — the same
    /// answer <c>AVG()</c> gives in SQL, and the one a row of zeros would silently destroy.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// An unknown column, no value column for an aggregate that needs one, or a column that turns
    /// out to hold text when asked to sum or average.
    /// </exception>
    public static QueryResultTable Pivot(
        QueryResultTable table, string groupBy, ResultAggregateKind kind,
        string? valueColumn = null, IEnumerable<ResultFilter>? filters = null)
    {
        var group = RequireColumn(table, groupBy);
        var rows = VisibleRows(table, filters);

        string? value = null;
        if (kind != ResultAggregateKind.Count)
        {
            if (string.IsNullOrWhiteSpace(valueColumn))
                throw new ArgumentException(
                    $"{kind.ToString().ToUpperInvariant()} needs a column to work on — pick one.");
            value = RequireColumn(table, valueColumn);
        }

        var groups = rows
            .GroupBy(r => Display(ValueOf(r, group)), StringComparer.Ordinal)
            .Select(g => (Key: g.Key, Cell: Aggregate(g.ToList(), kind, value)))
            .ToList();

        var aggregateName = kind.ColumnName(value);
        var result = new QueryResultTable
        {
            Title = $"Pivot: {aggregateName} by {group}",
            Columns = [group, aggregateName],
            Elapsed = table.Elapsed,
        };

        // Biggest first: the row a pivot exists to produce. A group whose aggregate is empty
        // (all its cells were NULL) sorts last and shows empty, rather than being read as a zero.
        foreach (var entry in groups
                     .OrderByDescending(e => e.Cell is null ? decimal.MinValue : SortKey(e.Cell))
                     .ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            result.Rows.Add(new Dictionary<string, object?>
            {
                [group] = entry.Key == NullText ? null : entry.Key,
                [aggregateName] = entry.Cell,
            });
        }

        return result;
    }

    /// <summary>
    /// The order a pivot row sorts into. Numbers and dates have one; anything else — text, a group
    /// with nothing to measure — sorts as the smallest, so a text MIN/MAX column ends up in
    /// group-name order, which is at least stable.
    /// </summary>
    private static decimal SortKey(object cell) => cell switch
    {
        DateTime d => d.Ticks,
        DateTimeOffset d => d.UtcTicks,
        DateOnly d => d.DayNumber,
        _ => AsNumber(cell) ?? decimal.MinValue,
    };

    /// <summary>The number one group reduces to. Null when an aggregate has nothing to measure.</summary>
    private static object? Aggregate(
        List<Dictionary<string, object?>> rows, ResultAggregateKind kind, string? valueColumn)
    {
        if (kind == ResultAggregateKind.Count) return rows.Count;

        var values = rows
            .Select(r => ValueOf(r, valueColumn!))
            .Where(v => v is not null)
            .ToList();

        return kind switch
        {
            ResultAggregateKind.Sum => values.Count == 0 ? null : Sum(values),
            ResultAggregateKind.Avg => values.Count == 0 ? null : Sum(values) / values.Count,
            ResultAggregateKind.Min => values.Count == 0 ? null : Extreme(values, first: true),
            ResultAggregateKind.Max => values.Count == 0 ? null : Extreme(values, first: false),
            _ => rows.Count,
        };
    }

    private static decimal Sum(List<object?> values)
    {
        decimal total = 0;
        foreach (var (value, index) in values.Select((v, i) => (v, i)))
        {
            total += AsNumber(value) ?? throw NotNumeric(index, value);
        }
        return total;
    }

    /// <summary>
    /// Smallest or largest value in its own type's order — a date compares as a date and a number
    /// as a number, which is what MIN/MAX mean. Mixed or unmixed-up types fall back to text order,
    /// because a result set that holds both <c>int</c> and <c>string</c> in one column has no
    /// ordering of its own to lose.
    /// </summary>
    private static object Extreme(List<object?> values, bool first)
    {
        var extreme = values[0]!;
        foreach (var candidate in values.Skip(1))
        {
            var relation = Compare(extreme, candidate!);
            if (relation is null)
                return first
                    ? values.Select(Display).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).First()
                    : values.Select(Display).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).Last();

            // relation is extreme-to-candidate: a new minimum is the one the current holder is
            // greater than, and the other way round for a maximum.
            if (first ? relation > 0 : relation < 0) extreme = candidate;
        }
        return extreme;
    }

    private static int? Compare(object left, object right)
    {
        var (a, b) = (AsNumber(left), AsNumber(right));
        if (a is not null && b is not null) return decimal.Compare(a.Value, b.Value);
        if (left is IComparable same && right.GetType() == left.GetType())
            return same.CompareTo(right);
        return null;
    }

    /// <summary>Every CLR number SqlClient hands back for a column, as one type.</summary>
    private static decimal? AsNumber(object? value) => value switch
    {
        byte n => n,
        short n => n,
        int n => n,
        long n => n,
        float n => (decimal)n,
        double n => (decimal)n,
        decimal n => n,
        _ => null,
    };

    private static ArgumentException NotNumeric(int rowIndex, object? value) =>
        new($"This column is not numbers — row {rowIndex + 1} holds “{Display(value)}”. " +
            "SUM and AVERAGE need a numeric column; COUNT works on any.");

    private static string RequireColumn(QueryResultTable table, string column)
    {
        var match = table.Columns.FirstOrDefault(c =>
            string.Equals(c, column, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new ArgumentException(
                $"“{column}” is not a column of this result set — it has {table.Columns.Count}.");
        return match;
    }
}
