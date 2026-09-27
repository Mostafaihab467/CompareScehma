using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SchemaCompare.Services;

namespace SchemaCompare.Models;

/// <summary>
/// One tabular result produced by a query batch: either a result set
/// (columns + rows) or an affected-rows summary for action statements.
/// </summary>
/// <remarks>
/// <see cref="Rows"/> is what the server sent and never changes; <see cref="VisibleRows"/> is
/// what the grid shows once the operator's column filters are applied. Everything that reads the
/// result — the grid, the copy buttons, every export, the pivot — goes through the visible set,
/// because a filter you can see but that copy ignores is the bug where someone emails 5 000 rows
/// they were never looking at.
/// </remarks>
public class QueryResultTable : ObservableObject
{
    public required string Title { get; init; }
    public List<string> Columns { get; init; } = [];
    public List<Dictionary<string, object?>> Rows { get; init; } = [];

    /// <summary>True when the service capped the rows to protect the UI.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>Set for action statements (INSERT/UPDATE/DELETE) instead of rows.</summary>
    public int? AffectedRows { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>One filter per column the operator narrowed. Absent means unconstrained.</summary>
    public List<ResultFilter> Filters { get; } = [];

    private ObservableCollection<Dictionary<string, object?>>? _visibleRows;

    /// <summary>
    /// What the grid, the copy and every export read. Until a filter narrows it this is simply
    /// <see cref="Rows"/>, seeded on first use so a result nobody filtered can never render empty.
    /// </summary>
    public ObservableCollection<Dictionary<string, object?>> VisibleRows =>
        _visibleRows ??= new ObservableCollection<Dictionary<string, object?>>(Rows);

    public int RowCount => Rows.Count;

    public bool HasFilters => Filters.Any(f => f.IsActive);

    /// <summary>Rows on screen, so an unfiltered result reports the server's count.</summary>
    public int VisibleRowCount => VisibleRows.Count;

    public string Summary =>
        AffectedRows.HasValue
            ? $"{AffectedRows.Value:N0} row(s) affected in {Elapsed.TotalSeconds:0.00}s"
            : HasFilters
                ? $"{VisibleRowCount:N0} of {RowCount:N0} row(s) shown in {Elapsed.TotalSeconds:0.00}s"
                : $"{RowCount:N0} row(s){(IsTruncated ? " (capped)" : string.Empty)} in {Elapsed.TotalSeconds:0.00}s";

    /// <summary>
    /// Re-read the filters and put the surviving rows on screen, in the order the server sent them.
    /// </summary>
    /// <returns>How many rows are now visible.</returns>
    public int ApplyFilters()
    {
        var kept = ResultGridService.VisibleRows(this, Filters);
        ShowRows(kept);
        return kept.Count;
    }

    /// <summary>The columns with something asked of them, as the status line names them.</summary>
    public string FilterNote =>
        HasFilters ? string.Join(" · ", Filters.Where(f => f.IsActive).Select(f => f.ToString())) : "";

    /// <summary>Drop every filter and show the whole result again.</summary>
    public void ClearFilters()
    {
        Filters.Clear();
        ShowRows(Rows);
    }

    /// <summary>
    /// The filter belonging to one column, created and empty if this is the first ask for it.
    /// </summary>
    public ResultFilter FilterFor(string column)
    {
        var existing = Filters.FirstOrDefault(f =>
            string.Equals(f.Column, column, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var created = new ResultFilter { Column = column };
        Filters.Add(created);
        return created;
    }

    /// <summary>Swap the visible rows without re-assigning ItemsSource, so the grid keeps its columns.</summary>
    private void ShowRows(IEnumerable<Dictionary<string, object?>> rows)
    {
        var shown = VisibleRows;
        shown.Clear();
        foreach (var row in rows) shown.Add(row);
        OnPropertyChanged(nameof(VisibleRowCount));
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(FilterNote));
        OnPropertyChanged(nameof(Summary));
    }
}
