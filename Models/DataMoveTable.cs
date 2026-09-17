using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

/// <summary>One table that can safely participate in a data synchronization.</summary>
public partial class DataMoveTable : ObservableObject
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public required List<string> PrimaryKeyColumns { get; init; }
    public required List<string> WritableColumns { get; init; }
    public long SourceRowCount { get; init; }
    public string? Warning { get; init; }
    [ObservableProperty] private bool _isSelected = true;

    public string FullName => $"[{Schema}].[{Name}]";
    public string PrimaryKeyText => PrimaryKeyColumns.Count == 0 ? "No primary key" : string.Join(", ", PrimaryKeyColumns);
    public string StatusText => Warning ?? $"{SourceRowCount:N0} source rows • PK: {PrimaryKeyText}";
    public bool IsCompatible => Warning is null;
}

public sealed class DataMovePlan
{
    public required List<DataMoveTable> Tables { get; init; }
    public required Dictionary<string, List<string>> ParentTables { get; init; }
    public required List<DataMoveRelation> Relations { get; init; }
}

/// <summary>A target foreign-key rule, including columns needed for source-data validation.</summary>
public sealed class DataMoveRelation
{
    public required string ConstraintName { get; init; }
    public required string ChildTable { get; init; }
    public required string ParentTable { get; init; }
    public required List<string> ChildColumns { get; init; }
    public required List<string> ParentColumns { get; init; }
}

public sealed record DataMoveResult(int TablesCompleted, long InsertedRows, long UpdatedRows);
