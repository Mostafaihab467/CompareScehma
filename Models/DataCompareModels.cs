using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

/// <summary>What a row-level, key-based comparison concluded about one table.</summary>
public enum TableDataState
{
    /// <summary>Not compared yet, or excluded by the user.</summary>
    Pending,
    /// <summary>Every key paired up and every compared column matched.</summary>
    Identical,
    /// <summary>Rows and/or column values differ.</summary>
    Different,
    /// <summary>The scan stopped at the row limit, so the verdict covers only the first N rows.</summary>
    Truncated,
    /// <summary>Deliberately skipped — no shared key, no table on one side, or unsupported.</summary>
    Skipped
}

public enum RowDiffKind { MissingInTarget, ExtraInTarget, Changed }

/// <summary>
/// One differing row, described by its key. The values themselves are only ever
/// written to the generated script, so a wide table does not mean a wide UI list.
/// </summary>
public sealed record RowDiff(RowDiffKind Kind, string KeyValue, string Detail)
{
    public string Action => Kind switch
    {
        RowDiffKind.MissingInTarget => "insert",
        RowDiffKind.ExtraInTarget => "delete",
        _ => "update"
    };
}

/// <summary>A table the comparison can consider, with its verdict once run.</summary>
public partial class DataCompareTable : ObservableObject
{
    public string Schema { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Shared primary-key columns, in key order. Empty means not comparable.</summary>
    public List<string> KeyColumns { get; init; } = [];

    /// <summary>Non-key columns present and writable on both sides.</summary>
    public List<string> CompareColumns { get; init; } = [];

    /// <summary>Why the table is not comparable, or null when it is.</summary>
    public string? SkipReason { get; init; }

    /// <summary>True when the two sides expose different column sets, so only the
    /// shared columns were compared.</summary>
    public bool PartialColumns { get; init; }

    [ObservableProperty] private bool _isSelected = true;

    private TableDataState _state = TableDataState.Pending;

    /// <summary>Setting it always refreshes the two text properties, so re-comparing a
    /// table that was already Different still repaints its counts.</summary>
    public TableDataState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged(nameof(State));
            }
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(VerdictText));
        }
    }

    /// <summary>Written by the view-model before it flips <see cref="State"/>, which is
    /// the only observed property — the two text properties below read all of them.</summary>
    public long SourceRows { get; set; }
    public long TargetRows { get; set; }
    public int MissingInTarget { get; set; }
    public int ExtraInTarget { get; set; }
    public int ChangedRows { get; set; }

    public string FullName => $"[{Schema}].[{Name}]";
    public bool IsComparable => SkipReason is null && KeyColumns.Count > 0;

    public string StatusText => State switch
    {
        TableDataState.Pending => IsComparable
            ? $"key {string.Join(", ", KeyColumns)} · {SourceRows:N0} source / {TargetRows:N0} target rows"
            : SkipReason ?? "Not comparable",
        TableDataState.Skipped => SkipReason ?? "Skipped",
        TableDataState.Identical => "Identical — every key paired and every compared column matched",
        TableDataState.Truncated => $"{MissingInTarget} missing, {ExtraInTarget} extra, {ChangedRows} changed in the first {SourceRows:N0} scanned rows (row limit reached)",
        _ => $"{MissingInTarget} missing in target, {ExtraInTarget} extra in target, {ChangedRows} changed"
    };

    /// <summary>Short verdict shown in the grid.</summary>
    public string VerdictText => State switch
    {
        TableDataState.Identical => "✓ identical",
        TableDataState.Different => "≠ different",
        TableDataState.Truncated => "≈ partial scan",
        TableDataState.Skipped => "— skipped",
        _ => "… pending"
    };
}

/// <summary>The outcome of comparing one table, including the statements that would
/// synchronize the target.</summary>
public sealed class TableDataDiff
{
    public required DataCompareTable Table { get; init; }
    public long SourceRows { get; init; }
    public long TargetRows { get; init; }
    public int MissingInTarget { get; init; }
    public int ExtraInTarget { get; init; }
    public int ChangedRows { get; init; }
    public bool Truncated { get; init; }

    /// <summary>Bounded sample of differing rows for the UI.</summary>
    public List<RowDiff> Rows { get; init; } = [];

    /// <summary>Statements that would make the target match the source. Deletions are
    /// only present when the caller asked for them.</summary>
    public List<string> Inserts { get; init; } = [];
    public List<string> Updates { get; init; } = [];
    public List<string> Deletes { get; init; } = [];

    public TableDataState State =>
        Truncated ? TableDataState.Truncated
        : MissingInTarget + ExtraInTarget + ChangedRows > 0 ? TableDataState.Different
        : TableDataState.Identical;

    public string Summary => State switch
    {
        TableDataState.Identical => "identical",
        TableDataState.Truncated => $"differs within the first {SourceRows:N0} scanned rows",
        _ => $"{MissingInTarget} to insert, {ExtraInTarget} to delete, {ChangedRows} to update"
    };
}
