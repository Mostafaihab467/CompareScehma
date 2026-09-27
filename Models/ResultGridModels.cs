namespace SchemaCompare.Models;

/// <summary>
/// What the operator asked one column of a result set to show.
/// </summary>
/// <remarks>
/// Two ways to narrow a column, and they are deliberately different: <see cref="Text"/> is a
/// hunt for something whose value is not known ("contains 42"), <see cref="Values"/> is a
/// choice among the values the result actually holds. A row survives when every active filter
/// accepts it, so columns AND together and the values inside one column OR together — the same
/// shape a <c>WHERE … IN (…)</c> has, which is what a DBA reads this as.
/// <para>
/// Values are keyed by their <em>displayed</em> text, because that is what the list shows and
/// what the operator ticks. Two cells that print the same are then one entry, which is the
/// honest reading of "the value as this grid renders it".
/// </para>
/// </remarks>
public sealed class ResultFilter
{
    public required string Column { get; init; }

    /// <summary>Case-insensitive substring the cell's text must contain. Empty asks for nothing.</summary>
    public string Text { get; set; } = "";

    /// <summary>Displayed values ticked from the column's own list. Empty asks for nothing.</summary>
    public HashSet<string> Values { get; } = new(StringComparer.Ordinal);

    public bool IsActive => !string.IsNullOrWhiteSpace(Text) || Values.Count > 0;

    public override string ToString() =>
        Values.Count > 0
            ? $"{Column} in ({string.Join(", ", Values)})"
            : $"{Column} contains “{Text}”";
}

/// <summary>One distinct value a column holds, with how many rows hold it.</summary>
/// <param name="Display">The value as the grid prints it, so the label and the tick agree.</param>
public sealed record ResultValue(string Display, int Count, bool IsNull);

/// <summary>
/// What a pivot does with the rows of one group.
/// </summary>
public enum ResultAggregateKind
{
    Count,
    Sum,
    Avg,
    Min,
    Max
}

/// <summary>
/// The operator's answer to "group by what, and do what with each group?" — the picker in
/// <c>Views/ResultPivotDialog</c> returns one of these, and nothing else decides the shape of
/// the pivot.
/// </summary>
public sealed class PivotRequest
{
    public required string GroupBy { get; init; }
    public ResultAggregateKind Kind { get; init; }

    /// <summary>The column SUM/AVERAGE/MIN/MAX work on. Null for COUNT, which needs none.</summary>
    public string? ValueColumn { get; init; }
}

/// <summary>
/// The <see cref="ResultAggregateKind"/> as the picker shows it — including whether the
/// aggregate needs a number to work on, which is the one thing the UI has to know before it
/// offers <c>SUM</c> on a text column.
/// </summary>
public static class ResultAggregates
{
    public static readonly ResultAggregateKind[] All =
    [
        ResultAggregateKind.Count, ResultAggregateKind.Sum, ResultAggregateKind.Avg,
        ResultAggregateKind.Min, ResultAggregateKind.Max
    ];

    public static string Label(this ResultAggregateKind kind) => kind switch
    {
        ResultAggregateKind.Count => "COUNT — how many rows",
        ResultAggregateKind.Sum => "SUM — total of a number column",
        ResultAggregateKind.Avg => "AVERAGE — mean of a number column",
        ResultAggregateKind.Min => "MINIMUM — smallest value",
        ResultAggregateKind.Max => "MAXIMUM — largest value",
        _ => kind.ToString()
    };

    /// <summary>True when the aggregate is only meaningful over numbers.</summary>
    public static bool NeedsNumber(this ResultAggregateKind kind) =>
        kind is ResultAggregateKind.Sum or ResultAggregateKind.Avg;

    public static string ColumnName(this ResultAggregateKind kind, string? valueColumn) => kind switch
    {
        ResultAggregateKind.Count => "Rows",
        _ => $"{kind.ToString().ToUpperInvariant()}({valueColumn})"
    };
}
