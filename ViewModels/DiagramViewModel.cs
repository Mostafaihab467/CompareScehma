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
    private CancellationTokenSource? _autosaveCts;

    public ObservableCollection<SavedConnection> SavedConnections { get; } = [];
    public ObservableCollection<DiagramTableNode> Tables { get; } = [];
    public ObservableCollection<DiagramTableNode> FilteredTables { get; } = [];
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
    [ObservableProperty] private double _canvasWidth = 2400;
    [ObservableProperty] private double _canvasHeight = 1600;
    [ObservableProperty] private string _countsText = string.Empty;

    /// <summary>Fired when nodes are added/removed or visibility changes — the view rebuilds cards.</summary>
    public event Action? StructureChanged;
    /// <summary>Fired when a node moved or dim state changed — the view refreshes visuals + links.</summary>
    public event Action? PositionsChanged;
    /// <summary>Fired when the view should scroll a node into view.</summary>
    public event Action<DiagramTableNode>? FocusRequested;

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
            foreach (var t in tables)
            {
                t.PropertyChanged += OnNodePropertyChanged;
                Tables.Add(t);
            }

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
    private void ZoomOut() => ZoomLevel = Math.Max(0.4, Math.Round(ZoomLevel - 0.1, 2));

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
        foreach (var t in Tables) t.IsVisible = false;
    }

    [RelayCommand]
    private void FocusTable(DiagramTableNode? node)
    {
        if (node is null) return;
        if (!node.IsVisible) node.IsVisible = true;
        FocusRequested?.Invoke(node);
    }

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
            t.IsDimmed = !match;
            if (match || string.IsNullOrEmpty(filter))
                FilteredTables.Add(t);
        }
        // When searching, still list matches only; dimming applies on canvas.
        if (!string.IsNullOrEmpty(filter))
        {
            // already filtered above
        }
        UpdateCounts();
        PositionsChanged?.Invoke();
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
        if (e.PropertyName is nameof(DiagramTableNode.X) or nameof(DiagramTableNode.Y))
        {
            UpdateCanvasSize();
            PositionsChanged?.Invoke();
            ScheduleAutoSave();
        }
        else if (e.PropertyName is nameof(DiagramTableNode.IsVisible))
        {
            UpdateCounts();
            StructureChanged?.Invoke();
            ScheduleAutoSave();
        }
        else if (e.PropertyName is nameof(DiagramTableNode.IsDimmed))
        {
            PositionsChanged?.Invoke();
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
                await Task.Delay(900, cts.Token);
                if (cts.Token.IsCancellationRequested) return;
                _persistence.Save(BuildState());
            }
            catch (OperationCanceledException) { }
            catch { /* auto-save must never interrupt diagram work */ }
        });
    }

    private void AutoSaveNow()
    {
        if (!HasSchema) return;
        try { _persistence.Save(BuildState()); }
        catch { /* auto-save must never interrupt diagram work */ }
    }
}
