namespace SchemaCompare.Models;

/// <summary>
/// One tabular result produced by a query batch: either a result set
/// (columns + rows) or an affected-rows summary for action statements.
/// </summary>
public sealed class QueryResultTable
{
    public required string Title { get; init; }
    public List<string> Columns { get; init; } = [];
    public List<Dictionary<string, object?>> Rows { get; init; } = [];

    /// <summary>True when the service capped the rows to protect the UI.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>Set for action statements (INSERT/UPDATE/DELETE) instead of rows.</summary>
    public int? AffectedRows { get; init; }

    public TimeSpan Elapsed { get; init; }

    public int RowCount => Rows.Count;

    public string Summary =>
        AffectedRows.HasValue
            ? $"{AffectedRows.Value:N0} row(s) affected in {Elapsed.TotalSeconds:0.00}s"
            : $"{RowCount:N0} row(s){(IsTruncated ? " (capped)" : string.Empty)} in {Elapsed.TotalSeconds:0.00}s";
}
