using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

/// <summary>
/// One CSV column as the import wizard sees it: where it came from, where it goes,
/// and what the wizard guessed about its type.
/// </summary>
public partial class ImportColumn : ObservableObject
{
    /// <summary>Header text in the file, or its position when the file has no header.</summary>
    public required string SourceName { get; init; }

    /// <summary>Position in the CSV record, used when the file carries no header row.</summary>
    public required int Position { get; init; }

    [ObservableProperty] private string _targetName = "";

    /// <summary>SQL type the wizard inferred; editable, since a guess is only a guess.</summary>
    [ObservableProperty] private string _sqlType = "nvarchar(50)";

    [ObservableProperty] private bool _include = true;

    [ObservableProperty] private bool _nullable = true;

    /// <summary>Why the type was chosen: the sample the inference actually saw.</summary>
    [ObservableProperty] private string _note = "";

    /// <summary>Set when importing into an existing table that has no such column.</summary>
    [ObservableProperty] private bool _missingOnTarget;

    /// <summary>Ticked by the wizard for the first column of a key-looking sample; only
    /// used when the table is being created, since an existing table has its own key.</summary>
    [ObservableProperty] private bool _isKey;

    public string Header => string.IsNullOrWhiteSpace(SourceName) ? $"Column {Position + 1}" : SourceName;
}

/// <summary>Columns of an existing target table, read so the mapping can be checked.</summary>
public sealed class TargetColumn
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required bool Nullable { get; init; }
    public required bool IsIdentity { get; init; }
    public required bool IsComputed { get; init; }
}

/// <summary>What a CSV file looks like before any of it is sent to a server.</summary>
public sealed class CsvTable
{
    public required IReadOnlyList<string> Headers { get; init; }

    /// <summary>Records below the header, capped at whatever sample size was asked for.</summary>
    public required IReadOnlyList<string?[]> Sample { get; init; }
}

/// <summary>The outcome of a load: what was written, and whether the table came with it.</summary>
public sealed class ImportResult
{
    public required int RowsLoaded { get; init; }
    public required bool TableCreated { get; init; }
}
