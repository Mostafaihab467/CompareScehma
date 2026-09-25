using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.ViewModels;

/// <summary>
/// Owns the dialog state for the Query Constructor window. Bridges the
/// QueryViewModel's connection + schema cache to the UI controls, regenerates
/// SQL on every change, and exposes Apply commands that push the generated SQL
/// back into a query tab.
///
/// Lifecycle: created by the QueryWindow, used for the lifetime of the dialog,
/// then discarded — no shared mutable state with the parent.
/// </summary>
public partial class QueryBuilderViewModel : ObservableObject
{
    private readonly QueryViewModel _parent;
    private readonly QueryBuilderService _service = new();

    public QueryBuilderModel Builder { get; } = new();

    /// <summary>Tables available to pick from in the dropdowns (loaded after connect).</summary>
    public ObservableCollection<QuerySchemaService.TableInfo> AvailableTables { get; } = new();

    /// <summary>Flat union of all column names across the joined tables — used for
    /// the WHERE / ORDER BY column dropdowns (a quick-search helper).</summary>
    public ObservableCollection<string> AvailableColumnNames { get; } = new();

    public ObservableCollection<AggregateFunction> Aggregates { get; } = new(Enum.GetValues<AggregateFunction>());
    public ObservableCollection<FilterOperatorOption> OperatorOptions { get; } = new(
        Enum.GetValues<FilterOperator>().Select(o => new FilterOperatorOption(o, o.ToFriendlyLabel())));
    public ObservableCollection<FilterConjunction> Conjunctions { get; } = new(Enum.GetValues<FilterConjunction>());
    public ObservableCollection<JoinTypeOption> JoinTypeOptions { get; } = new(
        Enum.GetValues<JoinType>().Select(j => new JoinTypeOption(j, j.ToFriendlyLabel())));
    public ObservableCollection<SortDirection> SortDirections { get; } = new(Enum.GetValues<SortDirection>());

    [ObservableProperty] private string _statusMessage = "Pick a table to begin.";
    [ObservableProperty] private bool   _isLoadingSchema;
    [ObservableProperty] private string _connectionLabel = "Not connected";

    // ─── Direct table pickers ──────────────────────────────────────────
    public void SetTable(BuilderTable table, QuerySchemaService.TableInfo? info)
    {
        if (info == null) return;
        table.SelectedTableInfo = info;
        table.Schema = info.Schema;
        table.Name = info.Name;
        if (string.IsNullOrWhiteSpace(table.Alias))
            table.Alias = info.Name.Length > 0 ? info.Name[..1].ToLowerInvariant() : "t";
        _ = OnTableChangedAsync(table);
    }

    public void SetJoinTable(JoinClause join, QuerySchemaService.TableInfo? info)
    {
        if (info == null) return;
        join.RightTable.SelectedTableInfo = info;
        join.RightTable.Schema = info.Schema;
        join.RightTable.Name = info.Name;
        if (string.IsNullOrWhiteSpace(join.RightTable.Alias))
            join.RightTable.Alias = $"t{Builder.Tables.Count + Builder.Joins.Count}";
        QueryBuilderService.PopulateColumns(new[] { join.RightTable },
            SchemaCompare.Controls.SqlCompletionProvider.ColumnsByTable);
        OnBuilderChanged();
    }

    /// <summary>True when there's something valid to apply.</summary>
    public bool CanApply => Builder.Tables.Count > 0;

    public QueryBuilderViewModel(QueryViewModel parent)
    {
        _parent = parent;
        ConnectionLabel = parent.ConnectedDatabaseLabel;

        // Reload AvailableTables from the parent's connection. The schema cache
        // (Tables / ColumnsByTable on SqlCompletionProvider) was populated by
        // QueryViewModel.RefreshSchemaCacheAsync after the parent connected.
        AvailableTables.Clear();
        foreach (var t in SchemaCompare.Controls.SqlCompletionProvider.Tables)
            AvailableTables.Add(t);

        // When the user changes anything in the model, regenerate SQL.
        Builder.Tables.CollectionChanged        += (_, _) => OnBuilderChanged();
        Builder.Joins.CollectionChanged         += (_, _) => OnBuilderChanged();
        Builder.Filters.CollectionChanged       += (_, _) => OnBuilderChanged();
        Builder.Having.CollectionChanged        += (_, _) => OnBuilderChanged();
        Builder.GroupBy.CollectionChanged       += (_, _) => OnBuilderChanged();
        Builder.OrderBy.CollectionChanged       += (_, _) => OnBuilderChanged();
        Builder.PropertyChanged                 += Builder_PropertyChanged;

        // Seed a single empty primary table row so the UI shows the FROM picker
        // immediately — the user can switch schema/name or just keep it empty.
        AddPrimaryTable();
        Regenerate();
    }

    // ────────────────────────────────────────────────────────────────────
    // Item-level change wiring
    // Without this, editing a bound field on a row (filter value, alias,
    // column…) never regenerated the SQL — only adding/removing rows did,
    // so the preview and "Apply" silently used stale SQL.
    // ────────────────────────────────────────────────────────────────────

    private readonly List<INotifyPropertyChanged> _wiredItems = [];

    private void Builder_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QueryBuilderModel.GeneratedSql))
            Regenerate();
    }

    private void ItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Regenerate();

    private void Wire(INotifyPropertyChanged? item)
    {
        if (item == null) return;
        item.PropertyChanged += ItemChanged;
        _wiredItems.Add(item);
    }

    private void WireTable(BuilderTable table)
    {
        Wire(table);
        table.SelectedColumns.CollectionChanged -= WiringCollectionChanged;
        table.SelectedColumns.CollectionChanged += WiringCollectionChanged;
        foreach (var c in table.SelectedColumns)
            Wire(c);
    }

    private void WiringCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => RebuildItemWiring();

    /// <summary>Drops and re-creates every item subscription — cheap at dialog scale
    /// and immune to missed remove events.</summary>
    private void RebuildItemWiring()
    {
        foreach (var item in _wiredItems)
            item.PropertyChanged -= ItemChanged;
        _wiredItems.Clear();

        foreach (var t in Builder.Tables)
            WireTable(t);
        foreach (var j in Builder.Joins)
            WireTable(j.RightTable);
        foreach (var f in Builder.Filters)
            Wire(f);
        foreach (var h in Builder.Having)
            Wire(h);
        foreach (var g in Builder.GroupBy)
            Wire(g);
        foreach (var o in Builder.OrderBy)
            Wire(o);
    }

    // ────────────────────────────────────────────────────────────────────
    // Builder mutation commands
    // ────────────────────────────────────────────────────────────────────

    [RelayCommand]
    public void AddPrimaryTable()
    {
        if (Builder.Tables.Count == 0)
            Builder.Tables.Add(new BuilderTable { Alias = "t1" });
        else
            AddTable();
    }

    [RelayCommand]
    public void AddTable()
    {
        var alias = $"t{Builder.Tables.Count + Builder.Joins.Count + 1}";
        Builder.Tables.Add(new BuilderTable { Alias = alias });
    }

    [RelayCommand]
    public void RemoveTable(BuilderTable? table)
    {
        if (table == null) return;
        Builder.Tables.Remove(table);
        // Also drop any joins that reference it.
        foreach (var j in Builder.Joins.Where(j => j.RightTable == table).ToList())
            Builder.Joins.Remove(j);
    }

    [RelayCommand]
    public void AddJoin()
    {
        var alias = $"t{Builder.Tables.Count + Builder.Joins.Count + 1}";
        Builder.Joins.Add(new JoinClause
        {
            RightTable = new BuilderTable { Alias = alias }
        });
    }

    [RelayCommand]
    public void RemoveJoin(JoinClause? join)
    {
        if (join == null) return;
        Builder.Joins.Remove(join);
    }

    [RelayCommand]
    public void AddFilter()
    {
        var alias = Builder.Tables.FirstOrDefault()?.Alias ?? "";
        Builder.Filters.Add(new FilterCondition { TableAlias = alias });
    }

    [RelayCommand]
    public void RemoveFilter(FilterCondition? filter)
    {
        if (filter == null) return;
        Builder.Filters.Remove(filter);
    }

    [RelayCommand]
    public void AddHaving()
    {
        var alias = Builder.Tables.FirstOrDefault()?.Alias ?? "";
        Builder.Having.Add(new HavingCondition
        {
            TableAlias = alias,
            Aggregate = AggregateFunction.CountStar,
            Operator = FilterOperator.GreaterThan
        });
    }

    [RelayCommand]
    public void RemoveHaving(HavingCondition? item)
    {
        if (item == null) return;
        Builder.Having.Remove(item);
    }

    [RelayCommand]
    public void AddGroupBy()
    {
        var t = Builder.Tables.FirstOrDefault();
        var col = t?.Columns.FirstOrDefault() ?? "";
        Builder.GroupBy.Add(new GroupByItem { TableAlias = t?.Alias ?? "", ColumnName = col });
    }

    [RelayCommand]
    public void RemoveGroupBy(GroupByItem? item)
    {
        if (item == null) return;
        Builder.GroupBy.Remove(item);
    }

    [RelayCommand]
    public void AddOrderBy()
    {
        var alias = Builder.Tables.FirstOrDefault()?.Alias ?? "";
        var firstCol = Builder.Tables.FirstOrDefault()?.Columns.FirstOrDefault() ?? "";
        Builder.OrderBy.Add(new OrderByItem { TableAlias = alias, ColumnName = firstCol });
    }

    [RelayCommand]
    public void RemoveOrderBy(OrderByItem? item)
    {
        if (item == null) return;
        Builder.OrderBy.Remove(item);
    }

    /// <summary>Auto-fills the SELECT column list with "alias.*" for a given table.</summary>
    [RelayCommand]
    public void SelectAllColumns(BuilderTable? table)
    {
        if (table == null) return;
        if (table.SelectedColumns.All(c => c.ColumnName == ""))
        {
            table.SelectedColumns.Clear();
            table.SelectedColumns.Add(new SelectedColumn { Table = table, TableAlias = table.Alias });
        }
        else
        {
            table.SelectedColumns.Clear();
        }
    }

    [RelayCommand]
    public void AddColumn(BuilderTable? table)
    {
        if (table == null) return;
        var col = table.Columns.FirstOrDefault() ?? "";
        table.SelectedColumns.Add(new SelectedColumn { Table = table, TableAlias = table.Alias, ColumnName = col });
    }

    [RelayCommand]
    public void RemoveColumn(SelectedColumn? column)
    {
        if (column == null) return;
        var owner = Builder.Tables.FirstOrDefault(t => t.SelectedColumns.Contains(column));
        owner?.SelectedColumns.Remove(column);
    }

    [RelayCommand]
    public void Reset()
    {
        Builder.Reset();
        AddPrimaryTable();
    }

    // ────────────────────────────────────────────────────────────────────
    // Schema sync + FK suggestion
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Called after a table is picked or its alias changes — populates Columns.</summary>
    public async Task OnTableChangedAsync(BuilderTable table)
    {
        QueryBuilderService.PopulateColumns(new[] { table },
            SchemaCompare.Controls.SqlCompletionProvider.ColumnsByTable);
        Regenerate();

        // Best-effort FK auto-suggest against the primary table.
        if (table == Builder.Tables.FirstOrDefault()
            && !string.IsNullOrWhiteSpace(table.Schema)
            && !string.IsNullOrWhiteSpace(table.Name)
            && _parent.IsConnected)
        {
            try
            {
                var info = _parent.SelectedConnection?.ToConnectionInfo();
                if (info == null) return;
                var joins = await _service.GetJoinSuggestionsAsync(info, table.Schema, table.Name);
                if (joins.Count == 0)
                {
                    StatusMessage = "No FK relationships found for this table. JOINs are still usable — pick ON columns manually.";
                    return;
                }
                // Pre-populate join dropdowns: only set if the user hasn't touched them.
                foreach (var j in joins)
                {
                    var alias = $"t{Builder.Tables.Count + Builder.Joins.Count + 1}";
                    Builder.Joins.Add(new JoinClause
                    {
                        JoinType = JoinType.Inner,
                        RightTable = new BuilderTable
                        {
                            Schema = j.RightSchema,
                            Name = j.RightTable,
                            Alias = alias
                        },
                        LeftColumn = $"{table.Alias}.{j.LeftColumn}",
                        RightColumn = $"{alias}.{j.RightColumn}"
                    });
                }
                QueryBuilderService.PopulateColumns(
                    Builder.Joins.Select(j => j.RightTable).Concat(new[] { table }),
                    SchemaCompare.Controls.SqlCompletionProvider.ColumnsByTable);
                StatusMessage = $"Auto-added {joins.Count} JOIN(s) from foreign keys.";
            }
            catch (Exception ex)
            {
                AppLog.Warn($"[QueryBuilder] FK auto-fill failed: {ex.Message}");
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // SQL regeneration
    // ────────────────────────────────────────────────────────────────────

    private void OnBuilderChanged()
    {
        // Ensure every table's Columns list reflects the latest schema.
        QueryBuilderService.PopulateColumns(Builder.Tables.Concat(Builder.Joins.Select(j => j.RightTable)),
            SchemaCompare.Controls.SqlCompletionProvider.ColumnsByTable);
        RebuildItemWiring();
        // Flat-list of every distinct column across the FROM/JOIN tables, used for
        // the WHERE/ORDER BY column dropdowns.
        AvailableColumnNames.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Builder.Tables.Concat(Builder.Joins.Select(j => j.RightTable)))
            foreach (var c in t.Columns)
                if (seen.Add(c)) AvailableColumnNames.Add(c);
        OnPropertyChanged(nameof(CanApply));
        Regenerate();
    }

    private void Regenerate()
    {
        Builder.GeneratedSql = SqlBuilder.Build(Builder);
        OnPropertyChanged(nameof(CanApply));
        WarnIncompleteConditions();
    }

    /// <summary>Conditions the user started filling but left half-done are silently
    /// skipped by the SQL generator — surface them instead of "nothing happens".</summary>
    private void WarnIncompleteConditions()
    {
        var incomplete =
            Builder.Filters.Count(f =>
                (!string.IsNullOrWhiteSpace(f.ColumnName) || !string.IsNullOrWhiteSpace(f.Value)) &&
                (string.IsNullOrWhiteSpace(f.ColumnName) || (f.NeedsValue && string.IsNullOrWhiteSpace(f.Value))))
            + Builder.Having.Count(h =>
                (!string.IsNullOrWhiteSpace(h.ColumnName) || h.Aggregate != AggregateFunction.CountStar || !string.IsNullOrWhiteSpace(h.Value)) &&
                ((h.Aggregate != AggregateFunction.CountStar && string.IsNullOrWhiteSpace(h.ColumnName)) ||
                 (h.NeedsValue && string.IsNullOrWhiteSpace(h.Value))));
        if (incomplete > 0)
            StatusMessage = $"SQL updated — {incomplete} condition(s) incomplete and left out of the query (pick their column / value).";
        else if (StatusMessage.Contains("incomplete"))
            StatusMessage = "SQL updated.";
    }

    // ────────────────────────────────────────────────────────────────────
    // Apply — invoked by the dialog when the user clicks "Apply"
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Result returned to the dialog so it can close itself.</summary>
    public enum ApplyTarget { NewTab, ActiveTab, Cancelled }

    /// <summary>
    /// Pushes the generated SQL into the parent QueryViewModel. Default = new tab.
    /// Returns the chosen target so the dialog can close without further work.
    /// </summary>
    public (ApplyTarget target, string sql) Apply(ApplyTarget target)
    {
        var sql = Builder.GeneratedSql;
        if (string.IsNullOrWhiteSpace(sql) || sql.StartsWith("--"))
            return (ApplyTarget.Cancelled, sql);

        if (target == ApplyTarget.ActiveTab && _parent.ActiveTab is { IsExecuting: false } active)
        {
            _parent.ReplaceActiveTabSql(sql);
        }
        else
        {
            _parent.NewTab(sql);
        }
        StatusMessage = target == ApplyTarget.ActiveTab
            ? "Query replaced in active tab."
            : "Query inserted into a new tab.";
        return (target, sql);
    }
}