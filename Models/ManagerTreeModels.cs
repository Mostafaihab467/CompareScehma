using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

/// <summary>Kind of node rendered in the DB Manager explorer tree.</summary>
public enum NodeKind
{
    Root,
    TablesFolder, ViewsFolder, ProcsFolder, FunctionsFolder, SearchFolder,
    Table, View, Proc, Function, TriggerObj,
    ColumnsFolder, KeysFolder, IndexesFolder, TriggersFolder, StatsFolder, PartitionsFolder,
    SecurityFolder,
    Column, Key, IndexObj, TriggerChild, Stat, Partition, DbUser, DbRole
}

/// <summary>
/// A single node of the DB Manager explorer tree (SSMS-style). Object folders load
/// their children lazily: a "Loading…" placeholder is present until the folder is
/// first expanded, then the real children replace it.
/// </summary>
public partial class ManagerNode : ObservableObject
{
    public NodeKind Kind { get; init; }
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Icon { get; init; } = "";

    /// <summary>Owning object schema (schema of the table this node belongs to).</summary>
    public string Schema { get; init; } = "";
    /// <summary>Owning object name ("" for top-level folders).</summary>
    public string Name { get; init; } = "";

    // Column leaf
    public bool IsPrimaryKey { get; init; }
    public bool IsForeignKey { get; init; }
    public string? ColumnType { get; init; }

    // Index leaf
    public string IndexType { get; init; } = "";
    public bool IsUnique { get; init; }
    public bool IsDisabled { get; init; }
    public bool IsPrimaryKeyIndex { get; init; }
    public string? IndexFilter { get; init; }

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;
    private ObservableCollection<ManagerNode> _children = [];
    public ObservableCollection<ManagerNode> Children
    {
        get => _children;
        set { SetProperty(ref _children, value); OnPropertyChanged(nameof(HasChildren)); }
    }

    /// <summary>Populates <see cref="Children"/> on first expand. Set by the tree builder.</summary>
    public Func<CancellationToken, Task>? Loader { get; set; }
    public bool HasLoaded { get; set; }

    public ManagerNode? Parent { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !HasLoaded && Loader != null)
        {
            HasLoaded = true; // one load per expand; refresh resets it
            _ = RunLoaderAsync();
        }
    }

    private async Task RunLoaderAsync()
    {
        var loader = Loader;
        if (loader == null) return;
        try
        {
            await loader(CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Children.Clear();
            Children.Add(new ManagerNode { Kind = NodeKind.Column, Label = "Error: " + ex.Message, Icon = "⚠" });
        }
    }

    /// <summary>Forces a reload regardless of expansion state (used by Refresh).</summary>
    public Task RunLoaderNow() => RunLoaderAsync();

    /// <summary>Whether the tree shows an expander for this node.</summary>
    public bool HasChildren => Kind is not (NodeKind.Column or NodeKind.Key or NodeKind.IndexObj
        or NodeKind.TriggerChild or NodeKind.Stat or NodeKind.Partition
        or NodeKind.DbUser or NodeKind.DbRole);

    // ─── Context-menu visibility flags ───
    public bool CanOpen => Kind is NodeKind.Table or NodeKind.View or NodeKind.Proc
        or NodeKind.Function or NodeKind.TriggerObj;
    public bool CanDatabaseProps => Kind is NodeKind.Root;
    public bool CanShrink => Kind is NodeKind.Root;
    public bool CanRestore => Kind is NodeKind.Root;
    public bool CanNewIndex => Kind is NodeKind.Table or NodeKind.IndexesFolder;
    public bool CanCreatePartition => Kind is NodeKind.Table;
    public bool CanScriptCreate => Kind is NodeKind.Table or NodeKind.View or NodeKind.Proc
        or NodeKind.Function or NodeKind.TriggerObj or NodeKind.IndexObj;
    public bool CanScriptData => Kind is NodeKind.Table or NodeKind.View;
    public bool CanScriptDelete => Kind is NodeKind.Table;
    public bool CanScriptExec => Kind is NodeKind.Proc or NodeKind.Function;
    public bool CanScriptCreateOrAlter => Kind is NodeKind.View or NodeKind.Proc
        or NodeKind.Function or NodeKind.TriggerObj;
    public bool CanScriptDrop => Kind is NodeKind.Table or NodeKind.View or NodeKind.Proc
        or NodeKind.Function or NodeKind.TriggerObj;
    public bool CanRebuildIndex => Kind is NodeKind.IndexObj && !IsDisabled;
    public bool CanDisableIndex => Kind is NodeKind.IndexObj && !IsDisabled && !IsPrimaryKeyIndex;
    public bool CanEnableIndex => Kind is NodeKind.IndexObj && IsDisabled;
    public bool CanDropIndex => Kind is NodeKind.IndexObj && !IsPrimaryKeyIndex;
    public bool CanUpdateStats => Kind is NodeKind.Stat or NodeKind.Table;
    public bool CanRefresh => Kind is NodeKind.Table or NodeKind.View
        or NodeKind.ColumnsFolder or NodeKind.KeysFolder or NodeKind.IndexesFolder
        or NodeKind.TriggersFolder or NodeKind.StatsFolder or NodeKind.PartitionsFolder;

    public override string ToString() => Label;
}

// ─────────────────────────────────────────────────────────────────────────────
// Table metadata returned by DbManagerService.GetTableMetadataAsync
// ─────────────────────────────────────────────────────────────────────────────

public record MetaKey(string Name, string Kind /* PK | UQ | FK */, List<string> Columns, string? ReferencedTable);

public record MetaIndex(
    string Name, string TypeDesc, bool IsUnique, bool IsPrimaryKey, bool IsUniqueConstraint,
    bool IsDisabled, string? Filter, List<string> KeyColumns, List<string> IncludedColumns);

public record MetaStat(string Name, long Rows, DateTime? LastUpdated);

public record MetaPartition(int Number, long Rows, string? FunctionName, string? SchemeName,
                            string? Boundary, string? Filegroup);

public class TableMetadata
{
    public string Schema { get; init; } = "";
    public string Name { get; init; } = "";
    public List<TableColumn> Columns { get; set; } = [];
    public List<MetaKey> Keys { get; set; } = [];
    public List<MetaIndex> Indexes { get; set; } = [];
    public List<string> Triggers { get; set; } = [];
    public List<MetaStat> Stats { get; set; } = [];
    public List<MetaPartition> Partitions { get; set; } = [];

    public bool IsPartitioned => Partitions.Any(p => p.SchemeName != null);
}
