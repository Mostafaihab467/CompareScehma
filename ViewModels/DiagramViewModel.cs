using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.ViewModels;

/// <summary>
/// View-model for the DB diagram window: dedicated connection picker,
/// schema loading, search/filter, zoom, auto-layout and layout persistence.
/// The canvas itself is rendered by the view, which subscribes to
/// <see cref="StructureChanged"/> and <see cref="PositionsChanged"/>.
/// </summary>
public partial class DiagramViewModel : ObservableObject
{
    private readonly DiagramSchemaService _schemaService = new();
    private readonly DiagramPersistenceService _persistence = new();
    private readonly SchemaCompareService _compareService = new();
    private readonly SavedConnectionsService _savedService = new();
    private bool _applyingProfile;
    private bool _suspendNodeEvents;
    private CancellationTokenSource? _autosaveCts;
    private Dictionary<string, (double X, double Y)>? _preIsolationPositions;
    private Dictionary<string, bool>? _preIsolationVisibility;
    private double _preIsolationZoom = 1.0;

    public ObservableCollection<SavedConnection> SavedConnections { get; } = [];
    public ObservableCollection<DiagramTableNode> Tables { get; } = [];
    public ObservableCollection<DiagramTableNode> FilteredTables { get; } = [];
    public ObservableCollection<DiagramRelationDisplayItem> ActiveTableRelations { get; } = [];
    public List<DiagramRelation> Relations { get; private set; } = [];

    [ObservableProperty] private string _server = "localhost";
    [ObservableProperty] private string _database = string.Empty;
    [ObservableProperty] private bool _useWindowsAuth = true;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private SavedConnection? _selectedSaved;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasSchema;
    [ObservableProperty] private string _statusMessage = "Pick a database and click Load schema.";
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private double _zoomLevel = 1.0;
    [ObservableProperty] private double _uiScale = AppSettings.DefaultUiScale;
    [ObservableProperty] private string? _isolatedTable;
    [ObservableProperty] private bool _showLinks = true;

    public bool IsIsolated => IsolatedTable is not null;
    public bool HasActiveTableRelations => ActiveTableRelations.Count > 0;
    public string IsolatedSummary => IsolatedTable is null
        ? string.Empty
        : $"({ActiveTableRelations.Count} FK relation(s), {BuildIsolationKeep()?.Count ?? 0} connected table(s)) • unrelated tables dimmed";
    public string LinksToggleText => ShowLinks ? "🔗 Links: On" : "🔗 Links: Off";
    [ObservableProperty] private double _canvasWidth = 2400;
    [ObservableProperty] private double _canvasHeight = 1600;
    [ObservableProperty] private string _countsText = string.Empty;

    /// <summary>Fired when nodes are added/removed or visibility changes — the view rebuilds cards.</summary>
    public event Action? StructureChanged;
    /// <summary>Fired when a node moved or dim state changed — the view refreshes visuals + links.</summary>
    public event Action? PositionsChanged;
    /// <summary>Fired when the view should scroll a node into view.</summary>
    public event Action<DiagramTableNode>? FocusRequested;
    /// <summary>Fired when the view should fit a collection of nodes at once into the viewport.</summary>
    public event Action<IReadOnlyList<DiagramTableNode>>? FitToNodesRequested;

    /// <summary>Set by the view: (suggestedFileName, defaultExtension) → chosen path or null.</summary>
    public Func<string, string, Task<string?>>? PickSaveFileAsync { get; set; }
    /// <summary>Set by the view: chosen file path or null.</summary>
    public Func<Task<string?>>? PickOpenFileAsync { get; set; }
    /// <summary>Set by the view for error-copy support.</summary>
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

    public string ZoomText => $"{ZoomLevel * 100:0}%";
    public string SuggestedFileName => $"{(string.IsNullOrWhiteSpace(Database) ? "diagram" : Database)}.diagram.json";

    public DiagramViewModel()
    {
        foreach (var saved in _savedService.Load())
            SavedConnections.Add(saved);
        try { UiScale = new AppSettingsService().Load().UiScale; }
        catch { /* default scale applies */ }
    }

    /// <summary>Prefills the picker from the main window's Source connection.</summary>
    public void InitializeFrom(string server, string database, bool useWindowsAuth, string username, string password)
    {
        _applyingProfile = true;
        try
        {
            Server = server;
            Database = database;
            UseWindowsAuth = useWindowsAuth;
            Username = username;
            Password = password;
            SelectedSaved = SavedConnections.FirstOrDefault(c => c.Matches(server, database, useWindowsAuth, username));
        }
        finally { _applyingProfile = false; }
    }

    partial void OnZoomLevelChanged(double value) => OnPropertyChanged(nameof(ZoomText));
    partial void OnIsolatedTableChanged(string? value)
    {
        OnPropertyChanged(nameof(IsIsolated));
        OnPropertyChanged(nameof(HasActiveTableRelations));
        OnPropertyChanged(nameof(IsolatedSummary));
        ApplyIsolation();
    }
    partial void OnShowLinksChanged(bool value)
    {
        OnPropertyChanged(nameof(LinksToggleText));
        PositionsChanged?.Invoke();
    }
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnIsLoadingChanged(bool value)
    {
        LoadSchemaCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnServerChanged(string value) => ClearSelectionOnManualEdit();
    partial void OnDatabaseChanged(string value) => ClearSelectionOnManualEdit();
    partial void OnUsernameChanged(string value) => ClearSelectionOnManualEdit();
    partial void OnPasswordChanged(string value) => ClearSelectionOnManualEdit();
    partial void OnUseWindowsAuthChanged(bool value) => ClearSelectionOnManualEdit();

    partial void OnSelectedSavedChanged(SavedConnection? value)
    {
        if (value is null || _applyingProfile) return;
        _applyingProfile = true;
        try
        {
            Server = value.Server;
            Database = value.Database;
            UseWindowsAuth = value.UseWindowsAuth;
            Username = value.Username;
            Password = value.Password;
        }
        finally { _applyingProfile = false; }
        ShowError = false;
        StatusMessage = $"Loaded saved profile '{value.DisplayName}'. Click Load schema.";
    }

    private void ClearSelectionOnManualEdit()
    {
        if (_applyingProfile) return;
        if (SelectedSaved is not null)
        {
            _applyingProfile = true;
            try { SelectedSaved = null; }
            finally { _applyingProfile = false; }
        }
    }

    private ConnectionInfo GetConnectionInfo() => new()
    {
        Server = Server?.Trim() ?? string.Empty,
        Database = Database?.Trim() ?? string.Empty,
        UseWindowsAuth = UseWindowsAuth,
        Username = Username?.Trim() ?? string.Empty,
        Password = Password ?? string.Empty,
    };

    private bool CanLoad() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadSchemaAsync()
    {
        var info = GetConnectionInfo();
        if (string.IsNullOrWhiteSpace(info.Server) || string.IsNullOrWhiteSpace(info.Database))
        {
            ErrorMessage = "Enter both server and database names before loading.";
            ShowError = true;
            return;
        }

        IsLoading = true;
        ShowError = false;
        HasSchema = false;
        StatusMessage = $"Loading schema from {info.Server}/{info.Database}…";
        try
        {
            var (tables, relations) = await _schemaService.GetSchemaAsync(info);
            DetachNodes();
            Tables.Clear();
            Relations = relations;
            ClearIsolationSilent();
            foreach (var t in tables)
            {
                t.PropertyChanged += OnNodePropertyChanged;
                Tables.Add(t);
            }
            ComputeRelationCounts();
            OnPropertyChanged(nameof(IsolatedSummary));

            HasSchema = Tables.Count > 0;

            // Restore the user's saved arrangement when available; otherwise auto-layout.
            var saved = _persistence.TryLoad(info.Server, info.Database);
            if (saved is not null)
            {
                var applied = _persistence.ApplyToTables(saved, Tables, out var zoom);
                ZoomLevel = zoom;
                if (applied == 0)
                    DiagramAutoLayoutService.ApplyLayout(Tables, Relations);
                StatusMessage = $"Loaded {Tables.Count} table(s), {Relations.Count} FK relation(s). Restored saved layout ({applied} placed).";
            }
            else
            {
                DiagramAutoLayoutService.ApplyLayout(Tables, Relations);
                StatusMessage = Tables.Count > 150
                    ? $"Loaded {Tables.Count} table(s), {Relations.Count} FK relation(s). Large schema — use search or hide tables you don't need."
                    : $"Loaded {Tables.Count} table(s), {Relations.Count} FK relation(s). Layout generated — drag tables to arrange.";
            }

            ApplyFilter();
            UpdateCanvasSize();
            UpdateCounts();
            StructureChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ShowError = true;
            StatusMessage = "Schema load failed.";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task TestConnectionAsync()
    {
        var info = GetConnectionInfo();
        StatusMessage = "Testing connection…";
        ShowError = false;
        try
        {
            StatusMessage = await _compareService.TestConnectionAsync(info);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ShowError = true;
            StatusMessage = "Connection test failed.";
        }
    }

    [RelayCommand]
    private void AutoLayout()
    {
        if (Tables.Count == 0) return;
        DiagramAutoLayoutService.ApplyLayout(Tables, Relations);
        UpdateCanvasSize();
        UpdateCounts();
        StructureChanged?.Invoke();
        AutoSaveNow();
        StatusMessage = "Auto-layout applied and saved.";
    }

    [RelayCommand]
    private void ResetView()
    {
        foreach (var t in Tables) t.IsVisible = true;
        ClearIsolationSilent();
        SearchText = string.Empty;
        ZoomLevel = 1.0;
        DiagramAutoLayoutService.ApplyLayout(Tables, Relations);
        UpdateCanvasSize();
        ApplyFilter();
        StructureChanged?.Invoke();
        AutoSaveNow();
        StatusMessage = "View reset: all tables visible, layout regenerated.";
    }

    [RelayCommand]
    private void ZoomIn() => ZoomLevel = Math.Min(2.0, Math.Round(ZoomLevel + 0.1, 2));

    [RelayCommand]
    private void ZoomOut() => ZoomLevel = Math.Max(0.1, Math.Round(ZoomLevel - 0.1, 2));

    [RelayCommand]
    private void ResetZoom() => ZoomLevel = 1.0;

    [RelayCommand]
    private void ShowAllTables()
    {
        foreach (var t in Tables) t.IsVisible = true;
    }

    [RelayCommand]
    private void HideAllTables()
    {
        if (IsolatedTable is not null) ClearIsolation();
        foreach (var t in Tables) t.IsVisible = false;
        StatusMessage = Tables.Count == 0 ? "No tables to hide." : $"Hidden all {Tables.Count} table(s).";
    }

    [RelayCommand]
    private void FocusTable(DiagramTableNode? node)
    {
        if (node is null) return;
        if (!node.IsVisible) node.IsVisible = true;
        FocusRequested?.Invoke(node);
    }

    /// <summary>
    /// Highlights a table's relations: auto-organizes the active table and its connected
    /// tables into a clean left-to-right flow, fits them all at once into the viewport,
    /// and dims unrelated tables. Clicking the same table again restores original positions and zoom.
    /// </summary>
    [RelayCommand]
    private void IsolateRelations(DiagramTableNode? node)
    {
        if (node is null) return;
        if (string.Equals(IsolatedTable, node.FullName, StringComparison.OrdinalIgnoreCase))
        {
            ClearIsolation();
            return;
        }
        var n = CountRelations(node.FullName);
        if (n == 0)
        {
            StatusMessage = $"{node.FullName} has no foreign-key relations.";
            return;
        }
        if (!node.IsVisible) node.IsVisible = true;

        if (_preIsolationPositions is null)
        {
            _preIsolationPositions = Tables.ToDictionary(t => t.FullName, t => (t.X, t.Y), StringComparer.OrdinalIgnoreCase);
            _preIsolationZoom = ZoomLevel;
        }
        _preIsolationVisibility ??= Tables.ToDictionary(t => t.FullName, t => t.IsVisible, StringComparer.OrdinalIgnoreCase);

        // Unhide the whole cluster first — like focusing each related table —
        // so every relation table appears on the canvas.
        var keep = BuildIsolationKeep(node.FullName);
        if (keep is not null)
        {
            _suspendNodeEvents = true;
            try
            {
                foreach (var name in keep)
                {
                    var t = Tables.FirstOrDefault(x => x.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (t is not null && !t.IsVisible) t.IsVisible = true;
                }
            }
            finally { _suspendNodeEvents = false; }
            StructureChanged?.Invoke();
        }

        IsolatedTable = node.FullName;

        if (keep is not null)
        {
            var relatedNodes = Tables.Where(t => keep.Contains(t.FullName) && !t.FullName.Equals(node.FullName, StringComparison.OrdinalIgnoreCase)).ToList();
            // Suspended: organizing must not spam refreshes or auto-save temporary positions.
            _suspendNodeEvents = true;
            try { DiagramAutoLayoutService.OrganizeCluster(node, relatedNodes, Relations); }
            finally { _suspendNodeEvents = false; }
            UpdateCanvasSize();
            PositionsChanged?.Invoke();

            // Fit only visible tables so hidden ones don't leave empty space in view.
            var visibleCluster = new List<DiagramTableNode> { node };
            visibleCluster.AddRange(relatedNodes.Where(t => t.IsVisible));
            FitToNodesRequested?.Invoke(visibleCluster);
        }

        StatusMessage = $"Organized relations for {node.FullName} ({n} FK(s)) — shown all at once in UI.";
    }

    [RelayCommand]
    private void ClearIsolation()
    {
        if (IsolatedTable is null) return;

        _suspendNodeEvents = true;
        try
        {
            if (_preIsolationVisibility is not null)
            {
                foreach (var (name, wasVisible) in _preIsolationVisibility)
                {
                    var table = Tables.FirstOrDefault(t => t.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (table is not null) table.IsVisible = wasVisible;
                }
                _preIsolationVisibility = null;
            }
            if (_preIsolationPositions is not null)
            {
                foreach (var (name, (x, y)) in _preIsolationPositions)
                {
                    var table = Tables.FirstOrDefault(t => t.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (table is not null)
                    {
                        table.X = x;
                        table.Y = y;
                    }
                }
                _preIsolationPositions = null;
                ZoomLevel = _preIsolationZoom;
            }
        }
        finally { _suspendNodeEvents = false; }

        IsolatedTable = null;
        UpdateCanvasSize();
        StructureChanged?.Invoke();
        AutoSaveNow();

        StatusMessage = "Restored all tables to normal view.";
    }

    [RelayCommand]
    private void FitAll()
    {
        var visible = Tables.Where(t => t.IsVisible).ToList();
        if (visible.Count == 0) return;
        FitToNodesRequested?.Invoke(visible);
        StatusMessage = $"Fitting all {visible.Count} table(s) at once onto the screen.";
    }

    [RelayCommand]
    private void OrganizeActiveCluster()
    {
        if (IsolatedTable is null) return;
        var active = Tables.FirstOrDefault(t => t.FullName.Equals(IsolatedTable, StringComparison.OrdinalIgnoreCase));
        if (active is null) return;
        var keep = BuildIsolationKeep();
        if (keep is null) return;

        var relatedNodes = Tables.Where(t => keep.Contains(t.FullName) && !t.FullName.Equals(active.FullName, StringComparison.OrdinalIgnoreCase)).ToList();
        _suspendNodeEvents = true;
        try { DiagramAutoLayoutService.OrganizeCluster(active, relatedNodes, Relations); }
        finally { _suspendNodeEvents = false; }
        UpdateCanvasSize();
        PositionsChanged?.Invoke();
        AutoSaveNow();

        var visibleCluster = new List<DiagramTableNode> { active };
        visibleCluster.AddRange(relatedNodes.Where(t => t.IsVisible));
        FitToNodesRequested?.Invoke(visibleCluster);
    }

    [RelayCommand]
    private void FocusRelatedTable(string? tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return;
        var node = Tables.FirstOrDefault(t => t.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
        if (node is not null)
        {
            if (!node.IsVisible) node.IsVisible = true;
            FocusRequested?.Invoke(node);
        }
    }

    private void ClearIsolationSilent()
    {
        IsolatedTable = null;
        _preIsolationPositions = null;
        _preIsolationVisibility = null;
    }

    [RelayCommand]
    private void ToggleLinks() => ShowLinks = !ShowLinks;

    [RelayCommand]
    private void SaveDiagram()
    {
        if (!HasSchema) return;
        try
        {
            _persistence.Save(BuildState());
            StatusMessage = $"Layout saved ({Tables.Count} table(s), zoom {ZoomText}). Reloads automatically next time.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ShowError = true;
        }
    }

    [RelayCommand]
    private async Task SaveDiagramAsAsync()
    {
        if (!HasSchema || PickSaveFileAsync is null) return;
        var path = await PickSaveFileAsync(SuggestedFileName, ".diagram.json");
        if (path is null) return;
        try
        {
            _persistence.SaveToFile(path, BuildState());
            StatusMessage = $"Layout file saved: {path}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ShowError = true;
        }
    }

    [RelayCommand]
    private async Task OpenDiagramFileAsync()
    {
        if (PickOpenFileAsync is null) return;
        var path = await PickOpenFileAsync();
        if (path is null) return;
        var state = _persistence.LoadFromFile(path);
        if (state is null)
        {
            ErrorMessage = $"Could not read a diagram layout from '{path}'.";
            ShowError = true;
            return;
        }
        if (Tables.Count == 0)
        {
            // Adopt the file's database as the picker target so Load fills the canvas.
            _applyingProfile = true;
            try
            {
                Server = state.Server;
                Database = state.Database;
                SelectedSaved = null;
            }
            finally { _applyingProfile = false; }
            ErrorMessage = $"Layout file is for {state.Server}/{state.Database}. Click Load schema — positions will be restored automatically.";
            ShowError = true;
            return;
        }
        var applied = _persistence.ApplyToTables(state, Tables, out var zoom);
        ZoomLevel = zoom;
        UpdateCanvasSize();
        StructureChanged?.Invoke();
        StatusMessage = $"Applied layout file: {applied} table(s) placed.";
    }

    [RelayCommand]
    private async Task CopyErrorAsync()
    {
        if (CopyToClipboardAsync is not null && !string.IsNullOrEmpty(ErrorMessage))
            await CopyToClipboardAsync(ErrorMessage);
    }

    private DiagramPersistedState BuildState() => new()
    {
        Server = Server?.Trim() ?? string.Empty,
        Database = Database?.Trim() ?? string.Empty,
        Zoom = ZoomLevel,
        Tables = Tables.Select(t => new DiagramTableState
        {
            FullName = t.FullName,
            X = t.X,
            Y = t.Y,
            IsVisible = t.IsVisible,
        }).ToList(),
    };

    private void ApplyFilter()
    {
        FilteredTables.Clear();
        var filter = SearchText?.Trim() ?? string.Empty;
        foreach (var t in Tables)
        {
            var match = string.IsNullOrEmpty(filter) ||
                        t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase);
            if (match || string.IsNullOrEmpty(filter))
                FilteredTables.Add(t);
        }
        RefreshDimming();
        UpdateCounts();
    }

    /// <summary>Updates table dimming based on both search filter and active table relation isolation.</summary>
    private void RefreshDimming()
    {
        var filter = SearchText?.Trim() ?? string.Empty;
        var keep = BuildIsolationKeep();
        foreach (var t in Tables)
        {
            var matchFilter = string.IsNullOrEmpty(filter) ||
                              t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase);
            var matchIsolation = keep is null || keep.Contains(t.FullName);
            t.IsDimmed = !matchFilter || !matchIsolation;
        }
        PositionsChanged?.Invoke();
    }

    /// <summary>Tables that stay bright while isolated: the active table plus its direct neighbours.</summary>
    private HashSet<string>? BuildIsolationKeep(string? isolatedTable = null)
    {
        var isolated = isolatedTable ?? IsolatedTable;
        if (isolated is null) return null;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { isolated };
        foreach (var r in Relations)
        {
            if (r.ChildTable.Equals(isolated, StringComparison.OrdinalIgnoreCase) ||
                r.ParentTable.Equals(isolated, StringComparison.OrdinalIgnoreCase))
            {
                keep.Add(r.ChildTable);
                keep.Add(r.ParentTable);
            }
        }
        return keep;
    }

    /// <summary>
    /// Highlights the active table and its directly related tables while dimming
    /// unrelated tables. Populates <see cref="ActiveTableRelations"/> for UI inspection.
    /// </summary>
    private void ApplyIsolation()
    {
        ActiveTableRelations.Clear();
        _suspendNodeEvents = true;
        try
        {
            foreach (var t in Tables)
            {
                t.IsIsolatedTarget = IsolatedTable is not null &&
                                     t.FullName.Equals(IsolatedTable, StringComparison.OrdinalIgnoreCase);
                t.IsHiddenByIsolation = false;
            }

            if (IsolatedTable is not null)
            {
                foreach (var r in Relations)
                {
                    if (r.ChildTable.Equals(IsolatedTable, StringComparison.OrdinalIgnoreCase))
                    {
                        ActiveTableRelations.Add(new DiagramRelationDisplayItem
                        {
                            ConstraintName = r.ConstraintName,
                            ChildTable = r.ChildTable,
                            ParentTable = r.ParentTable,
                            ChildColumns = r.ChildColumns,
                            ParentColumns = r.ParentColumns,
                            IsOutgoing = true,
                            OtherTable = r.ParentTable
                        });
                    }
                    else if (r.ParentTable.Equals(IsolatedTable, StringComparison.OrdinalIgnoreCase))
                    {
                        ActiveTableRelations.Add(new DiagramRelationDisplayItem
                        {
                            ConstraintName = r.ConstraintName,
                            ChildTable = r.ChildTable,
                            ParentTable = r.ParentTable,
                            ChildColumns = r.ChildColumns,
                            ParentColumns = r.ParentColumns,
                            IsOutgoing = false,
                            OtherTable = r.ChildTable
                        });
                    }
                }
            }
        }
        finally { _suspendNodeEvents = false; }

        OnPropertyChanged(nameof(HasActiveTableRelations));
        OnPropertyChanged(nameof(IsolatedSummary));
        RefreshDimming();
    }

    private int CountRelations(string fullName) => Relations.Count(r =>
        r.ChildTable.Equals(fullName, StringComparison.OrdinalIgnoreCase) ||
        r.ParentTable.Equals(fullName, StringComparison.OrdinalIgnoreCase));

    private void ComputeRelationCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Relations)
        {
            counts[r.ChildTable] = counts.GetValueOrDefault(r.ChildTable) + 1;
            counts[r.ParentTable] = counts.GetValueOrDefault(r.ParentTable) + 1;
        }
        foreach (var t in Tables)
        {
            var count = counts.GetValueOrDefault(t.FullName);
            t.RelationCount = count;

            if (count == 0)
            {
                t.RelationsTooltipText = $"{t.FullName}\nNo foreign-key relations";
            }
            else
            {
                var outgoing = Relations.Where(r => r.ChildTable.Equals(t.FullName, StringComparison.OrdinalIgnoreCase)).ToList();
                var incoming = Relations.Where(r => r.ParentTable.Equals(t.FullName, StringComparison.OrdinalIgnoreCase)).ToList();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{t.FullName} • {count} relation(s)");
                if (outgoing.Count > 0)
                {
                    sb.AppendLine($"\n↗ References ({outgoing.Count}):");
                    foreach (var r in outgoing)
                        sb.AppendLine($"  • {r.ParentTable} ({string.Join(", ", r.ChildColumns)} → {string.Join(", ", r.ParentColumns)})");
                }
                if (incoming.Count > 0)
                {
                    sb.AppendLine($"\n↙ Referenced by ({incoming.Count}):");
                    foreach (var r in incoming)
                        sb.AppendLine($"  • {r.ChildTable} ({string.Join(", ", r.ChildColumns)} → {string.Join(", ", r.ParentColumns)})");
                }
                t.RelationsTooltipText = sb.ToString().TrimEnd();
            }
        }
    }

    private void UpdateCounts()
    {
        var visible = Tables.Count(t => t.IsVisible);
        CountsText = Tables.Count == 0
            ? string.Empty
            : $"{Tables.Count} table(s) • {Relations.Count} FK(s) • {visible} visible";
    }

    private void UpdateCanvasSize()
    {
        if (Tables.Count == 0)
        {
            CanvasWidth = 2400;
            CanvasHeight = 1600;
            return;
        }
        var maxX = Tables.Max(t => t.X + t.NodeWidth);
        var maxY = Tables.Max(t => t.Y + t.NodeHeight);
        CanvasWidth = Math.Max(1600, maxX + 300);
        CanvasHeight = Math.Max(1000, maxY + 300);
    }

    private void DetachNodes()
    {
        foreach (var t in Tables)
            t.PropertyChanged -= OnNodePropertyChanged;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suspendNodeEvents) return;
        if (e.PropertyName is nameof(DiagramTableNode.X) or nameof(DiagramTableNode.Y))
        {
            UpdateCanvasSize();
            PositionsChanged?.Invoke();
            ScheduleAutoSave();
        }
        else if (e.PropertyName is nameof(DiagramTableNode.IsVisible))
        {
            if (sender is DiagramTableNode n)
            {
                // Keep the restore snapshot faithful to manual checkbox changes.
                if (_preIsolationVisibility is not null)
                    _preIsolationVisibility[n.FullName] = n.IsVisible;
                if (!n.IsVisible &&
                    string.Equals(IsolatedTable, n.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    ClearIsolation();
                    return;
                }
            }
            UpdateCounts();
            StructureChanged?.Invoke();
            ScheduleAutoSave();
        }
        else if (e.PropertyName is nameof(DiagramTableNode.IsDimmed) or nameof(DiagramTableNode.IsIsolatedTarget))
        {
            PositionsChanged?.Invoke();
        }
        else if (e.PropertyName is nameof(DiagramTableNode.IsHiddenByIsolation))
        {
            // Canvas rebuild only — isolation is session-only and never auto-saved.
            StructureChanged?.Invoke();
        }
    }

    private void ScheduleAutoSave()
    {
        if (!HasSchema) return;
        _autosaveCts?.Cancel();
        _autosaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _autosaveCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(900, cts.Token).ConfigureAwait(false);
                if (cts.Token.IsCancellationRequested) return;
                _persistence.Save(BuildState());
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ReportAutoSaveFailure(ex); }
        }).ConfigureAwait(false);
    }

    private void AutoSaveNow()
    {
        if (!HasSchema) return;
        try { _persistence.Save(BuildState()); }
        catch (Exception ex) { ReportAutoSaveFailure(ex); }
    }

    /// <summary>A failed auto-save must not interrupt diagram work, but it must not vanish either.</summary>
    private void ReportAutoSaveFailure(Exception ex)
    {
        AppLog.Error("DiagramViewModel", ex, "Diagram auto-save failed; the stored layout is out of date");
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            StatusMessage = "⚠ Diagram layout could not be saved — check folder permissions and move a box to retry.");
    }
}
