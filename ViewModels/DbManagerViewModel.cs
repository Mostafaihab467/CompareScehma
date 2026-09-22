using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.ViewModels;

public enum ManagerTab { Data, Structure, Definition, Execute }

public partial class DbManagerViewModel : ObservableObject
{
    private readonly DbManagerService _service = new();
    private readonly SavedConnectionsService _savedService = new();

    private CancellationTokenSource? _cts;

    // -------------------------------------------------------------------------
    // Connection
    // -------------------------------------------------------------------------
    public ObservableCollection<SavedConnection> SavedConnections { get; } = [];
    [ObservableProperty] private SavedConnection? _selectedConnection;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectedDatabaseLabel = "Not connected";

    // -------------------------------------------------------------------------
    // Object tree
    // -------------------------------------------------------------------------
    public ObservableCollection<DbObjectGroup> ObjectGroups { get; } = [];
    [ObservableProperty] private DbObjectInfo? _selectedObject;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoadingTree;

    // -------------------------------------------------------------------------
    // Active tab
    // -------------------------------------------------------------------------
    [ObservableProperty] private ManagerTab _activeTab = ManagerTab.Data;
    [ObservableProperty] private bool _isTabData = true;
    [ObservableProperty] private bool _isTabStructure;
    [ObservableProperty] private bool _isTabDefinition;
    [ObservableProperty] private bool _isTabExecute;
    [ObservableProperty] private bool _showExecuteTab;   // only for stored procs

    // -------------------------------------------------------------------------
    // Data tab
    // -------------------------------------------------------------------------
    public ObservableCollection<string> TableColumnNames { get; } = [];
    public ObservableCollection<Dictionary<string, object?>> TableRows { get; } = [];
    public ObservableCollection<TableFilter> TableFilters { get; } = [];

    [ObservableProperty] private int _topNRows = 500;
    [ObservableProperty] private string _dataStatusText = string.Empty;
    [ObservableProperty] private int _loadedRowCount;
    [ObservableProperty] private long _totalTableRowCount;
    [ObservableProperty] private bool _hasRowCountInfo;
    [ObservableProperty] private bool _isLoadingData;
    [ObservableProperty] private bool _hasTableData;

    // Row editing / deletion
    [ObservableProperty] private Dictionary<string, object?>? _selectedRow;
    [ObservableProperty] private bool _hasSelectedRow;
    [ObservableProperty] private bool _hasPendingEdits;
    private readonly Dictionary<Dictionary<string, object?>, Dictionary<string, object?>> _pendingRowEdits = new();
    private readonly Dictionary<Dictionary<string, object?>, Dictionary<string, object?>> _originalRowValues = new();

    public ICommand SaveTableChangesCommand { get; }
    public ICommand DeleteRowCommand { get; }

    public static int[] TopNOptions => [100, 200, 500, 1000, 5000, 0]; // 0 = ALL

    // -------------------------------------------------------------------------
    // Structure tab
    // -------------------------------------------------------------------------
    public ObservableCollection<TableColumn> StructureColumns { get; } = [];
    [ObservableProperty] private bool _hasStructure;

    // -------------------------------------------------------------------------
    // Definition tab (View / Modify SQL)
    // -------------------------------------------------------------------------
    [ObservableProperty] private string _objectDefinition = string.Empty;
    [ObservableProperty] private bool _hasDefinition;
    [ObservableProperty] private bool _isSavingDefinition;
    [ObservableProperty] private string _definitionSaveStatus = string.Empty;

    // -------------------------------------------------------------------------
    // Execute tab (Stored Procs)
    // -------------------------------------------------------------------------
    public ObservableCollection<StoredProcParam> ProcParams { get; } = [];
    public ObservableCollection<Dictionary<string, object?>> ExecResultRows { get; } = [];
    public ObservableCollection<string> ExecResultColumns { get; } = [];
    [ObservableProperty] private string _execOutputText = string.Empty;
    [ObservableProperty] private bool _hasExecResults;
    [ObservableProperty] private bool _isExecuting;

    // -------------------------------------------------------------------------
    // General status / error
    // -------------------------------------------------------------------------
    [ObservableProperty] private string _statusMessage = "Select a connection and click Connect.";
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Set by the window to enable clipboard operations.</summary>
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------
    public ICommand ConnectCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SelectObjectCommand { get; }
    public ICommand ApplyFiltersCommand { get; }
    public ICommand AddFilterCommand { get; }
    public ICommand RemoveFilterCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ExecuteProcCommand { get; }
    public ICommand CopyDefinitionCommand { get; }
    public ICommand SaveDefinitionCommand { get; }
    public ICommand SwitchToDataTabCommand { get; }
    public ICommand SwitchToStructureTabCommand { get; }
    public ICommand SwitchToDefinitionTabCommand { get; }
    public ICommand SwitchToExecuteTabCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public DbManagerViewModel()
    {
        ConnectCommand              = new AsyncRelayCommand(ConnectAsync);
        RefreshCommand              = new AsyncRelayCommand(RefreshAsync);
        SelectObjectCommand         = new AsyncRelayCommand<DbObjectInfo?>(SelectObjectAsync);
        ApplyFiltersCommand         = new AsyncRelayCommand(ApplyFiltersAsync);
        AddFilterCommand            = new RelayCommand(AddFilter);
        RemoveFilterCommand         = new RelayCommand<TableFilter?>(RemoveFilter);
        ClearFiltersCommand         = new RelayCommand(ClearFilters);
        ExecuteProcCommand          = new AsyncRelayCommand(ExecuteProcAsync);
        CopyDefinitionCommand       = new AsyncRelayCommand(CopyDefinitionAsync);
        SaveDefinitionCommand       = new AsyncRelayCommand(SaveDefinitionAsync);
        SwitchToDataTabCommand      = new AsyncRelayCommand(() => SwitchTabAsync(ManagerTab.Data));
        SwitchToStructureTabCommand = new AsyncRelayCommand(() => SwitchTabAsync(ManagerTab.Structure));
        SwitchToDefinitionTabCommand = new AsyncRelayCommand(() => SwitchTabAsync(ManagerTab.Definition));
        SwitchToExecuteTabCommand   = new AsyncRelayCommand(() => SwitchTabAsync(ManagerTab.Execute));
        SearchCommand               = new AsyncRelayCommand(SearchAsync);
        ClearSearchCommand          = new AsyncRelayCommand(ClearSearchAsync);
        SaveTableChangesCommand     = new AsyncRelayCommand(SaveTableChangesAsync, () => HasPendingEdits);
        DeleteRowCommand            = new AsyncRelayCommand(DeleteSelectedRowAsync, () => HasSelectedRow);

        LoadSavedConnections();
    }

    partial void OnSelectedRowChanged(Dictionary<string, object?>? value)
    {
        HasSelectedRow = value != null;
        ((AsyncRelayCommand)DeleteRowCommand).NotifyCanExecuteChanged();
    }

    partial void OnHasPendingEditsChanged(bool value)
    {
        ((AsyncRelayCommand)SaveTableChangesCommand).NotifyCanExecuteChanged();
    }

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------
    private void LoadSavedConnections()
    {
        var list = _savedService.Load();
        foreach (var c in list)
            SavedConnections.Add(c);
        SelectedConnection = SavedConnections.FirstOrDefault();
    }

    // -------------------------------------------------------------------------
    // Connect / refresh
    // -------------------------------------------------------------------------
    private async Task ConnectAsync()
    {
        if (SelectedConnection == null) return;
        await RunSafeAsync(async ct =>
        {
            IsLoadingTree = true;
            StatusMessage = $"Connecting to {SelectedConnection.DisplayName}…";
            var info = SelectedConnection.ToConnectionInfo();
            await LoadObjectTreeAsync(info, ct);
            IsConnected = true;
            ConnectedDatabaseLabel = $"{info.Server} / {info.Database}";
            StatusMessage = "Connected. Click an object to explore.";
        });
        IsLoadingTree = false;
    }

    private async Task RefreshAsync()
    {
        if (SelectedConnection == null || !IsConnected) return;
        await RunSafeAsync(async ct =>
        {
            IsLoadingTree = true;
            StatusMessage = "Refreshing…";
            var info = SelectedConnection.ToConnectionInfo();
            await LoadObjectTreeAsync(info, ct);
            StatusMessage = "Object tree refreshed.";
        });
        IsLoadingTree = false;
    }

    private async Task LoadObjectTreeAsync(ConnectionInfo info, CancellationToken ct)
    {
        ObjectGroups.Clear();
        var types = new[]
        {
            (DbObjectType.Table,           "Tables",             "🗂"),
            (DbObjectType.View,            "Views",              "👁"),
            (DbObjectType.StoredProcedure, "Stored Procedures",  "⚙"),
            (DbObjectType.Function,        "Functions",          "𝑓"),
            (DbObjectType.Trigger,         "Triggers",           "⚡"),
        };

        foreach (var (type, label, icon) in types)
        {
            ct.ThrowIfCancellationRequested();
            var items = await _service.GetObjectsAsync(info, type, ct);
            var group = new DbObjectGroup
            {
                GroupName  = label,
                ObjectType = type,
                Icon       = icon,
                IsExpanded = type is DbObjectType.Table or DbObjectType.StoredProcedure
            };
            group.SetItems(items);
            ObjectGroups.Add(group);
        }
    }

    // -------------------------------------------------------------------------
    // Object selection & tab loading
    // -------------------------------------------------------------------------
    partial void OnSelectedObjectChanged(DbObjectInfo? value)
    {
        ((AsyncRelayCommand)ApplyFiltersCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ExecuteProcCommand).NotifyCanExecuteChanged();
        if (value == null) return;
        var preferredTab = value.ObjectType switch
        {
            DbObjectType.Table or DbObjectType.View => ManagerTab.Data,
            DbObjectType.StoredProcedure            => ManagerTab.Execute,
            _                                       => ManagerTab.Definition
        };
        _ = SwitchTabAsync(preferredTab);
    }

    private async Task SelectObjectAsync(DbObjectInfo? obj)
    {
        if (obj == null) return;
        SelectedObject = obj;
    }

    private async Task SwitchTabAsync(ManagerTab tab)
    {
        ActiveTab        = tab;
        IsTabData        = tab == ManagerTab.Data;
        IsTabStructure   = tab == ManagerTab.Structure;
        IsTabDefinition  = tab == ManagerTab.Definition;
        IsTabExecute     = tab == ManagerTab.Execute;
        ShowExecuteTab   = SelectedObject?.ObjectType == DbObjectType.StoredProcedure;

        if (SelectedObject == null || !IsConnected || SelectedConnection == null) return;
        var info = SelectedConnection.ToConnectionInfo();
        var obj  = SelectedObject;

        switch (tab)
        {
            case ManagerTab.Data:
                await LoadTableDataAsync(info, obj);
                break;
            case ManagerTab.Structure:
                await LoadStructureAsync(info, obj);
                break;
            case ManagerTab.Definition:
                await LoadDefinitionAsync(info, obj);
                break;
            case ManagerTab.Execute:
                await LoadProcParamsAsync(info, obj);
                break;
        }
    }

    // -------------------------------------------------------------------------
    private string? _currentTableKey;

    private async Task LoadTableDataAsync(ConnectionInfo info, DbObjectInfo obj)
    {
        await RunSafeAsync(async ct =>
        {
            IsLoadingData = true;
            HasTableData  = false;
            TableRows.Clear();

            var tableKey = $"{obj.Schema}.{obj.Name}";
            if (!string.Equals(_currentTableKey, tableKey, StringComparison.OrdinalIgnoreCase))
            {
                _currentTableKey = tableKey;
                TableFilters.Clear();
                TableColumnNames.Clear();
            }

            StatusMessage = $"Loading data from {obj.DisplayName}…";

            var (cols, rows) = await _service.GetTableDataAsync(
                info, obj.Schema, obj.Name, TableFilters, TopNRows, ct);

            if (!TableColumnNames.SequenceEqual(cols))
            {
                var savedSelections = TableFilters.Select(f => f.ColumnName).ToList();
                TableColumnNames.Clear();
                foreach (var c in cols) TableColumnNames.Add(c);
                for (int i = 0; i < TableFilters.Count && i < savedSelections.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(savedSelections[i]) && cols.Contains(savedSelections[i]))
                        TableFilters[i].ColumnName = savedSelections[i];
                }
            }

            foreach (var r in rows) TableRows.Add(r);

            // Discard any pending in-memory edits for rows that no longer exist
            _pendingRowEdits.Clear();
            _originalRowValues.Clear();
            HasPendingEdits = false;
            ((AsyncRelayCommand)SaveTableChangesCommand).NotifyCanExecuteChanged();

            LoadedRowCount = TableRows.Count;
            HasTableData   = TableRows.Count > 0;

            if (obj.ObjectType == DbObjectType.Table)
            {
                try
                {
                    TotalTableRowCount = await _service.GetRowCountAsync(info, obj.Schema, obj.Name, ct);
                    HasRowCountInfo = true;
                    DataStatusText = $"{LoadedRowCount:N0} row{(LoadedRowCount == 1 ? "" : "s")} displayed (out of {TotalTableRowCount:N0} total) — {obj.DisplayName}";
                }
                catch
                {
                    TotalTableRowCount = LoadedRowCount;
                    HasRowCountInfo = true;
                    DataStatusText = $"{LoadedRowCount:N0} row{(LoadedRowCount == 1 ? "" : "s")} — {obj.DisplayName}";
                }
            }
            else
            {
                TotalTableRowCount = LoadedRowCount;
                HasRowCountInfo = true;
                DataStatusText = $"{LoadedRowCount:N0} row{(LoadedRowCount == 1 ? "" : "s")} — {obj.DisplayName}";
            }

            StatusMessage = DataStatusText;
            IsLoadingData = false;
        });
        IsLoadingData = false;
    }

    // -------------------------------------------------------------------------
    // Row editing / save / delete (Data tab)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by the view when a cell edit begins, before the new value is
    /// committed to the row, so Save Changes can build a correct WHERE clause.
    /// </summary>
    public void SnapshotRow(Dictionary<string, object?> row)
    {
        if (!_originalRowValues.ContainsKey(row))
            _originalRowValues[row] = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns the pre-edit value captured by <see cref="SnapshotRow"/>.</summary>
    public object? GetOriginalCellValue(Dictionary<string, object?> row, string columnName)
    {
        if (_originalRowValues.TryGetValue(row, out var snap) && snap.TryGetValue(columnName, out var v))
            return v;
        return row.TryGetValue(columnName, out var cur) ? cur : null;
    }

    /// <summary>
    /// Called by the view (CellEditEnding) after a cell edit is committed so the
    /// change can be flushed to the database when the user clicks Save Changes.
    /// </summary>
    public void CaptureCellEdit(Dictionary<string, object?> row, string columnName, object? newValue)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return;
        if (!_pendingRowEdits.TryGetValue(row, out var changes))
        {
            changes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            _pendingRowEdits[row] = changes;
        }
        // Fall back to a snapshot now if BeginningEdit wasn't observed
        SnapshotRow(row);
        changes[columnName] = newValue is string s && string.Equals(s.Trim(), "NULL", StringComparison.OrdinalIgnoreCase)
            ? null
            : newValue;

        HasPendingEdits = true;
        ((AsyncRelayCommand)SaveTableChangesCommand).NotifyCanExecuteChanged();
        StatusMessage = "Pending change — click 💾 Save Changes to write to the database.";
    }

    private async Task SaveTableChangesAsync()
    {
        if (SelectedConnection == null || SelectedObject == null)
        {
            StatusMessage = "Connect and select a table first.";
            return;
        }
        if (!_pendingRowEdits.Any())
        {
            StatusMessage = "No pending changes.";
            return;
        }

        var info = SelectedConnection.ToConnectionInfo();
        var obj  = SelectedObject;

        await RunSafeAsync(async ct =>
        {
            var cols   = await _service.GetTableColumnsAsync(info, obj.Schema, obj.Name, ct);
            var pkCols = cols.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

            if (pkCols.Count == 0)
            {
                StatusMessage = "⚠ Cannot save — table has no primary key.";
                return;
            }

            int saved = 0;
            foreach (var (row, changes) in _pendingRowEdits.ToList())
            {
                if (!_originalRowValues.TryGetValue(row, out var original)) continue;

                // Validate: all PK values must be present
                var pkValues = new Dictionary<string, object?>();
                var missingPk = false;
                foreach (var pk in pkCols)
                {
                    if (!original.TryGetValue(pk, out var v) || v == null || v is DBNull)
                    { missingPk = true; break; }
                    pkValues[pk] = ConvertForDb(cols, pk, v);
                }
                if (missingPk)
                {
                    StatusMessage = "⚠ Skipped a row — its primary key value is null.";
                    continue;
                }

                // Only columns that actually changed and are not PK/identity
                var setCols = changes
                    .Where(kv => !pkCols.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                    .Where(kv => !ValuesEqual(
                        original.TryGetValue(kv.Key, out var ov) ? ov : null,
                        kv.Value))
                    .ToDictionary(kv => kv.Key, kv => ConvertForDb(cols, kv.Key, kv.Value), StringComparer.OrdinalIgnoreCase);

                if (setCols.Count == 0) continue;

                await _service.UpdateRowAsync(info, obj.Schema, obj.Name, pkValues, setCols, ct);
                saved++;
            }

            _pendingRowEdits.Clear();
            _originalRowValues.Clear();
            HasPendingEdits = false;
            ((AsyncRelayCommand)SaveTableChangesCommand).NotifyCanExecuteChanged();

            StatusMessage = $"✓ {saved} row(s) saved to {obj.DisplayName}. Reloading…";
            await LoadTableDataAsync(info, obj);
        });
    }

    private async Task DeleteSelectedRowAsync()
    {
        if (SelectedConnection == null || SelectedObject == null || SelectedRow == null)
            return;
        var info = SelectedConnection.ToConnectionInfo();
        var obj  = SelectedObject;
        var row  = SelectedRow;

        await RunSafeAsync(async ct =>
        {
            var cols   = await _service.GetTableColumnsAsync(info, obj.Schema, obj.Name, ct);
            var pkCols = cols.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

            if (pkCols.Count == 0)
            {
                StatusMessage = "⚠ Cannot delete — table has no primary key.";
                return;
            }

            var pkValues = new Dictionary<string, object?>();
            foreach (var pk in pkCols)
            {
                if (!row.TryGetValue(pk, out var v) || v == null || v is DBNull)
                {
                    StatusMessage = "⚠ Cannot delete — primary key value is null for the selected row.";
                    return;
                }
                pkValues[pk] = ConvertForDb(cols, pk, v);
            }

            await _service.DeleteRowAsync(info, obj.Schema, obj.Name, pkValues, ct);
            _pendingRowEdits.Remove(row);
            _originalRowValues.Remove(row);

            StatusMessage = $"✓ Row deleted from {obj.DisplayName}. Reloading…";
            await LoadTableDataAsync(info, obj);
        });
    }

    private async Task ApplyFiltersAsync()
    {
        if (SelectedConnection == null)
        {
            StatusMessage = "Please select a connection and connect first.";
            return;
        }
        if (SelectedObject == null)
        {
            StatusMessage = "Please select a table or view from the left panel first.";
            return;
        }

        // Remove empty filter rows before querying
        var emptyFilters = TableFilters.Where(f => string.IsNullOrWhiteSpace(f.ColumnName)).ToList();
        foreach (var ef in emptyFilters)
            TableFilters.Remove(ef);

        await LoadTableDataAsync(SelectedConnection.ToConnectionInfo(), SelectedObject);
    }

    private void AddFilter()
    {
        var col = TableColumnNames.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;
        TableFilters.Add(new TableFilter { ColumnName = col });
    }

    private void RemoveFilter(TableFilter? filter)
    {
        if (filter != null) TableFilters.Remove(filter);
    }

    private void ClearFilters()
    {
        TableFilters.Clear();
        _ = ApplyFiltersAsync();
    }

    /// <summary>
    /// Compares an original cell value with an edited one (treating null/DBNull
    /// and the "(NULL)" display placeholder as equal) so unchanged cells are skipped.
    /// </summary>
    private static bool ValuesEqual(object? original, object? edited)
    {
        if (original is DBNull) original = null;
        if (edited   is DBNull) edited   = null;
        var origText = original switch
        {
            null   => string.Empty,
            string => (string)original,
            _      => original.ToString() ?? string.Empty
        };
        var editText = edited switch
        {
            null        => string.Empty,
            string es   => es,
            _           => edited.ToString() ?? string.Empty
        };
        if (string.Equals(editText, "(NULL)", StringComparison.OrdinalIgnoreCase)) editText = string.Empty;
        if (string.Equals(origText, "(NULL)", StringComparison.OrdinalIgnoreCase)) origText = string.Empty;
        return string.Equals(origText.Trim(), editText.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts a user-entered cell string to the appropriate CLR type for the
    /// column so SQL Server receives typed parameters (falls back to the raw string).
    /// </summary>
    private static object? ConvertForDb(List<TableColumn> cols, string columnName, object? value)
    {
        if (value is null or DBNull) return DBNull.Value;
        if (value is not string s) return value;
        if (string.Equals(s.Trim(), "NULL", StringComparison.OrdinalIgnoreCase)) return DBNull.Value;

        var col = cols.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
        var t   = col?.DataType.ToLowerInvariant();
        try
        {
            return t switch
            {
                "int" or "smallint"                                    => int.Parse(s),
                "bigint"                                               => long.Parse(s),
                "tinyint"                                              => byte.Parse(s),
                "bit"                                                  => s is "1" or "true" or "TRUE" or "True",
                "decimal" or "numeric" or "money" or "smallmoney"      => decimal.Parse(s),
                "float"                                                => double.Parse(s),
                "real"                                                 => float.Parse(s),
                "uniqueidentifier"                                     => Guid.Parse(s),
                "datetime" or "datetime2" or "smalldatetime" or "date" => DateTime.Parse(s),
                _ => s
            };
        }
        catch
        {
            return s; // let SQL Server surface a descriptive conversion error
        }
    }

    // -------------------------------------------------------------------------
    // Structure tab
    // -------------------------------------------------------------------------
    private async Task LoadStructureAsync(ConnectionInfo info, DbObjectInfo obj)
    {
        await RunSafeAsync(async ct =>
        {
            StructureColumns.Clear();
            HasStructure = false;
            StatusMessage = $"Loading structure of {obj.DisplayName}…";

            var cols = await _service.GetTableColumnsAsync(info, obj.Schema, obj.Name, ct);
            foreach (var c in cols) StructureColumns.Add(c);
            HasStructure = StructureColumns.Count > 0;
            StatusMessage = $"{StructureColumns.Count} column(s) — {obj.DisplayName}";
        });
    }

    // -------------------------------------------------------------------------
    // Definition tab (View / Modify SQL)
    // -------------------------------------------------------------------------
    private async Task LoadDefinitionAsync(ConnectionInfo info, DbObjectInfo obj)
    {
        await RunSafeAsync(async ct =>
        {
            HasDefinition    = false;
            ObjectDefinition = string.Empty;
            DefinitionSaveStatus = string.Empty;
            StatusMessage    = $"Loading definition of {obj.DisplayName}…";

            var def = await _service.GetObjectDefinitionAsync(info, obj.Schema, obj.Name, obj.ObjectType, ct);
            ObjectDefinition = def;
            HasDefinition    = !string.IsNullOrWhiteSpace(def);
            StatusMessage    = $"Definition loaded — {obj.DisplayName}";
        });
    }

    private async Task CopyDefinitionAsync()
    {
        if (CopyToClipboardAsync != null && !string.IsNullOrWhiteSpace(ObjectDefinition))
            await CopyToClipboardAsync(ObjectDefinition);
    }

    private async Task SaveDefinitionAsync()
    {
        if (SelectedConnection == null || SelectedObject == null || string.IsNullOrWhiteSpace(ObjectDefinition)) return;
        var info = SelectedConnection.ToConnectionInfo();
        var obj = SelectedObject;

        await RunSafeAsync(async ct =>
        {
            IsSavingDefinition = true;
            DefinitionSaveStatus = "Executing ALTER/CREATE script…";
            StatusMessage = $"Applying modified definition for {obj.DisplayName}…";

            await _service.ExecuteRawScriptAsync(info, ObjectDefinition, ct);

            DefinitionSaveStatus = $"✓ Updated successfully at {DateTime.Now:HH:mm:ss}";
            StatusMessage = $"Saved {obj.DisplayName} successfully.";
        });
        IsSavingDefinition = false;
    }

    // -------------------------------------------------------------------------
    // Execute tab (Stored Procedure)
    // -------------------------------------------------------------------------
    private async Task LoadProcParamsAsync(ConnectionInfo info, DbObjectInfo obj)
    {
        if (obj.ObjectType != DbObjectType.StoredProcedure) return;
        await RunSafeAsync(async ct =>
        {
            ProcParams.Clear();
            ExecResultRows.Clear();
            ExecResultColumns.Clear();
            ExecOutputText = string.Empty;
            HasExecResults = false;
            StatusMessage  = $"Loading parameters of {obj.DisplayName}…";

            var parms = await _service.GetProcedureParamsAsync(info, obj.Schema, obj.Name, ct);
            foreach (var p in parms) ProcParams.Add(p);
            StatusMessage = parms.Count > 0
                ? $"{parms.Count} parameter(s) — fill values and click Execute."
                : "No parameters — click Execute to run.";
        });
    }

    private async Task ExecuteProcAsync()
    {
        if (SelectedObject == null || SelectedConnection == null) return;
        var obj  = SelectedObject;
        var info = SelectedConnection.ToConnectionInfo();

        await RunSafeAsync(async ct =>
        {
            IsExecuting = true;
            ExecResultRows.Clear();
            ExecResultColumns.Clear();
            ExecOutputText = string.Empty;
            HasExecResults = false;
            StatusMessage  = $"Executing {obj.DisplayName}…";

            var (cols, rows, outputs, messages) =
                await _service.ExecuteProcedureAsync(info, obj.Schema, obj.Name, ProcParams, ct);

            foreach (var c in cols) ExecResultColumns.Add(c);
            foreach (var r in rows) ExecResultRows.Add(r);
            HasExecResults = ExecResultRows.Count > 0 || outputs.Count > 0;

            var sb = new System.Text.StringBuilder();
            if (messages.Length > 0) sb.AppendLine("--- Messages ---\n" + messages);
            if (outputs.Count > 0)
            {
                sb.AppendLine("--- Output Parameters ---");
                foreach (var kv in outputs)
                    sb.AppendLine($"  {kv.Key} = {kv.Value ?? "NULL"}");
            }
            ExecOutputText = sb.ToString().Trim();
            StatusMessage = $"Procedure executed — {rows.Count} row(s) returned.";
            IsExecuting = false;
        });
        IsExecuting = false;
    }

    // -------------------------------------------------------------------------
    // Search
    // -------------------------------------------------------------------------
    private async Task SearchAsync()
    {
        if (SelectedConnection == null || string.IsNullOrWhiteSpace(SearchText)) return;
        await RunSafeAsync(async ct =>
        {
            IsLoadingTree = true;
            StatusMessage = $"Searching for \"{SearchText}\"…";
            var info    = SelectedConnection.ToConnectionInfo();
            var results = await _service.SearchObjectsAsync(info, SearchText, ct);

            ObjectGroups.Clear();
            if (results.Count == 0)
            {
                StatusMessage = "No objects matched the search.";
                IsLoadingTree = false;
                return;
            }

            var grouped = results.GroupBy(r => r.ObjectType);
            foreach (var g in grouped)
            {
                var grp = new DbObjectGroup
                {
                    GroupName  = g.Key.ToString(),
                    ObjectType = g.Key,
                    IsExpanded = true
                };
                grp.SetItems(g);
                ObjectGroups.Add(grp);
            }
            StatusMessage = $"Found {results.Count} object(s) matching \"{SearchText}\".";
            IsLoadingTree = false;
        });
        IsLoadingTree = false;
    }

    private async Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        if (IsConnected && SelectedConnection != null)
        {
            await RunSafeAsync(async ct =>
            {
                IsLoadingTree = true;
                var info = SelectedConnection.ToConnectionInfo();
                await LoadObjectTreeAsync(info, ct);
                StatusMessage = "Search cleared.";
            });
            IsLoadingTree = false;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private async Task RunSafeAsync(Func<CancellationToken, Task> action)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ShowError  = false;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            await action(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ShowError    = true;
            StatusMessage = "Error — see details above.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Notify CanExecute when key properties change
    partial void OnSelectedConnectionChanged(SavedConnection? value)
    {
        ((AsyncRelayCommand)ConnectCommand).NotifyCanExecuteChanged();
    }

    partial void OnIsConnectedChanged(bool value)
    {
        ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ApplyFiltersCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ClearSearchCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ExecuteProcCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SaveDefinitionCommand).NotifyCanExecuteChanged();
    }

    partial void OnIsExecutingChanged(bool value)
    {
        ((AsyncRelayCommand)ExecuteProcCommand).NotifyCanExecuteChanged();
    }

    partial void OnHasDefinitionChanged(bool value)
    {
        ((AsyncRelayCommand)CopyDefinitionCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SaveDefinitionCommand).NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingDataChanged(bool value)
    {
        ((AsyncRelayCommand)ApplyFiltersCommand).NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
    }
}
