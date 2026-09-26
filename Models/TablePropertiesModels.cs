namespace SchemaCompare.Models;

/// <summary>Read-only per-table properties snapshot backing TablePropertiesDialog
/// (DbManagerService.GetTablePropertiesAsync). Sizes are KB, like sp_spaceused.</summary>
public sealed class TableProperties
{
    public string Schema { get; init; } = "";
    public string Name { get; init; } = "";
    public long Rows { get; init; }

    public long ReservedKb { get; init; }
    public long DataKb { get; init; }
    public long IndexKb { get; init; }
    public long UnusedKb { get; init; }

    public List<TableColumn> Columns { get; init; } = [];
    public TablePkInfo? PrimaryKey { get; init; }
    public List<TableForeignKeyRow> ForeignKeys { get; init; } = [];
    public List<TableForeignKeyRow> ReferencedBy { get; init; } = [];
    public List<TableCheckRow> Checks { get; init; } = [];
    public List<TableIndexRow> Indexes { get; init; } = [];
    public List<TableTriggerRow> Triggers { get; init; } = [];
    public List<TableStatRow> Statistics { get; init; } = [];
    public TablePartitionInfo? Partitioning { get; init; }
    public List<MetaPartition> Partitions { get; init; } = [];

    public string FullName => $"{Schema}.{Name}";
    public string ReservedText => MbText(ReservedKb);
    public string DataText => MbText(DataKb);
    public string IndexText => MbText(IndexKb);
    public string UnusedText => MbText(UnusedKb);
    public string RowsText => Rows.ToString("N0");
    public string SpaceSummaryText =>
        $"Reserved {ReservedText} — data {DataText}, indexes {IndexText}, unused {UnusedText}";
    public string PrimaryKeyText => PrimaryKey == null
        ? "none"
        : $"{PrimaryKey.Name} ({string.Join(", ", PrimaryKey.Columns)})";
    public string PartitioningText => Partitioning == null
        ? "not partitioned"
        : $"scheme [{Partitioning.SchemeName}] — function [{Partitioning.FunctionName}] ({Partitioning.FunctionTypeDesc})";

    private static string MbText(long kb) => $"{kb / 1024.0:N1} MB";
}

public sealed class TablePkInfo
{
    public string Name { get; init; } = "";
    public List<string> Columns { get; init; } = [];
}

/// <summary>One foreign key. Outbound rows set <see cref="ReferencedTable"/>;
/// inbound rows (this table is referenced) set <see cref="ParentTable"/>.</summary>
public sealed class TableForeignKeyRow
{
    public string Name { get; init; } = "";
    public string Columns { get; init; } = "";
    public string? ReferencedTable { get; init; }
    public string? ReferencedColumns { get; init; }
    public string? ParentTable { get; init; }
    public string? ParentColumns { get; init; }
}

public sealed class TableCheckRow
{
    public string Name { get; init; } = "";
    public string Definition { get; init; } = "";
}

public sealed class TableIndexRow
{
    public string Name { get; init; } = "";
    public string TypeDesc { get; init; } = "";
    public bool IsUnique { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsDisabled { get; init; }
    public string KeyColumns { get; init; } = "";
    public string IncludedColumns { get; init; } = "";
    public long UserSeeks { get; init; }
    public long UserScans { get; init; }
    public long UserLookups { get; init; }
    public long UserUpdates { get; init; }
    public string UniqueText => IsUnique ? "✔" : "";
    public string DisabledText => IsDisabled ? "DISABLED" : "";
    public string KindText => IsPrimaryKey ? "PRIMARY KEY" : TypeDesc.Replace('_', ' ');
}

public sealed class TableTriggerRow
{
    public string Name { get; init; } = "";
    public string Timing { get; init; } = "";
    public string Events { get; init; } = "";
    public bool IsDisabled { get; init; }
    public string DisabledText => IsDisabled ? "disabled" : "enabled";
}

public sealed class TableStatRow
{
    public string Name { get; init; } = "";
    public string Columns { get; init; } = "";
    public long Rows { get; init; }
    public bool Filtered { get; init; }
    public DateTime? LastUpdated { get; init; }
    public string RowsText => Rows.ToString("N0");
    public string UpdatedText => LastUpdated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "never sampled";
    public string FilterText => Filtered ? "filtered" : "";
}

public sealed class TablePartitionInfo
{
    public string SchemeName { get; init; } = "";
    public string FunctionName { get; init; } = "";
    public string FunctionTypeDesc { get; init; } = "";
}
