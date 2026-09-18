using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SchemaCompareService _compareService = new();
    private readonly DataMoveService _dataMoveService = new();
    private readonly DatabaseBackupService _backupService = new();
    private readonly SavedConnectionsService _savedService = new();
    private bool _applyingProfile;

    [ObservableProperty] private string _sourceServer = "localhost";
    [ObservableProperty] private string _sourceDatabase = "EgyptMart";
    [ObservableProperty] private bool _sourceUseWindowsAuth = true;
    [ObservableProperty] private string _sourceUsername = "";
    [ObservableProperty] private string _sourcePassword = "";

    [ObservableProperty] private string _targetServer = "192.168.1.161";
    [ObservableProperty] private string _targetDatabase = "EgyptMart";
    [ObservableProperty] private bool _targetUseWindowsAuth = false;
    [ObservableProperty] private string _targetUsername = "eta";
    [ObservableProperty] private string _targetPassword = "500600";

    [ObservableProperty] private bool _isComparing;
    [ObservableProperty] private string _statusMessage = "Enter connection details and click Compare";
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isMoveDataPage;
    [ObservableProperty] private bool _isSidebarOpen = true;
    [ObservableProperty] private GridLength _sidebarWidth = new(220);
    [ObservableProperty] private bool _hasDataMovePlan;
    [ObservableProperty] private string _dataMoveSummary = "Choose the shared Source and Target databases above, then analyze tables.";
    [ObservableProperty] private string _dataMoveRelationshipSummary = "Foreign-key dependencies will be checked before data is written.";
    [ObservableProperty] private bool _showDataMoveConfirmation;
    [ObservableProperty] private string _dataMoveFilterText = "";
    [ObservableProperty] private bool _backupUsesSource = true;
    [ObservableProperty] private string _backupDestinationPath = "";
    [ObservableProperty] private bool _isBackingUp;
    [ObservableProperty] private string _backupStatus = "Choose a database and a local .bacpac file.";

    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _resultSummaryText = "";
    [ObservableProperty] private SchemaDiffItem? _selectedDiff;
    [ObservableProperty] private string _diffScriptContent = "";
    [ObservableProperty] private string _sourceScriptContent = "";
    [ObservableProperty] private string _targetScriptContent = "";
    [ObservableProperty] private string _selectedObjectName = "";
    [ObservableProperty] private string _selectedObjectType = "";
    [ObservableProperty] private string _fullDeployScript = "";

    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _hasFullScript;
    [ObservableProperty] private bool _hasSourceScript;
    [ObservableProperty] private bool _hasTargetScript;
    [ObservableProperty] private bool _showApplyConfirmation;
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorMessage = "";

    [ObservableProperty] private string _sourceConnectionStatus = "";
    [ObservableProperty] private string _targetConnectionStatus = "";
    [ObservableProperty] private bool _sourceConnectionOk;
    [ObservableProperty] private bool _targetConnectionOk;
    [ObservableProperty] private bool _sourceHasConnectionResult;
    [ObservableProperty] private bool _targetHasConnectionResult;

    [ObservableProperty] private int _totalAdded;
    [ObservableProperty] private int _totalChanged;
    [ObservableProperty] private int _totalDeleted;
    [ObservableProperty] private string _addedFilterText = "Added: 0";
    [ObservableProperty] private string _changedFilterText = "Changed: 0";
    [ObservableProperty] private string _deletedFilterText = "Deleted: 0";

    // Operation logs
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _hasLogs;
    [ObservableProperty] private bool _showLogsDialog;
    [ObservableProperty] private int _logCount;

    public string LogsButtonText => $"Logs ({LogCount})";
    public string LogsDialogTitle => LogCount == 0 ? "Operation Logs" : $"Operation Logs ({LogCount})";

    partial void OnLogCountChanged(int value)
    {
        OnPropertyChanged(nameof(LogsButtonText));
        OnPropertyChanged(nameof(LogsDialogTitle));
    }

    // Saved credential profiles (shared by Source + Target dropdowns)
    public ObservableCollection<SavedConnection> SavedConnections { get; } = [];
    [ObservableProperty] private SavedConnection? _selectedSavedSource;
    [ObservableProperty] private SavedConnection? _selectedSavedTarget;

    public ObservableCollection<SchemaDiffItem> Differences { get; } = [];
    public ObservableCollection<SchemaDiffItem> FilteredDifferences { get; } = [];
    public ObservableCollection<DiffGroup> GroupedDifferences { get; } = [];
    public ObservableCollection<DataMoveTable> DataMoveTables { get; } = [];
    public ObservableCollection<DataMoveTable> FilteredDataMoveTables { get; } = [];
    private DataMovePlan? _dataMovePlan;

    [ObservableProperty] private bool _showAdded = true;
    [ObservableProperty] private bool _showChanged = true;
    [ObservableProperty] private bool _showDeleted = true;
    [ObservableProperty] private bool _showTargetPane = true;
    [ObservableProperty] private bool _showSourcePane = true;

    public ICommand CompareCommand { get; }
    public ICommand GenerateScriptCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand ConfirmApplyCommand { get; }
    public ICommand CancelApplyCommand { get; }
    public ICommand TestSourceConnectionCommand { get; }
    public ICommand TestTargetConnectionCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand ExpandAllDiffGroupsCommand { get; }
    public ICommand CollapseAllDiffGroupsCommand { get; }
    public ICommand ToggleDiffGroupCommand { get; }
    public ICommand ToggleTargetPaneCommand { get; }
    public ICommand ToggleSourcePaneCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand CloseLogsCommand { get; }
    public ICommand SaveSourceProfileCommand { get; }
    public ICommand SaveTargetProfileCommand { get; }
    public ICommand DeleteSavedProfileCommand { get; }
    public ICommand OpenSchemaCompareCommand { get; }
    public ICommand OpenMoveDataCommand { get; }
    public ICommand OpenBackupCommand { get; }
    public ICommand OpenDiagramCommand { get; }
    public ICommand ExportBackupCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand CopyErrorCommand { get; }
    public ICommand AnalyzeDataMoveCommand { get; }
    public ICommand StartDataMoveCommand { get; }
    public ICommand ConfirmDataMoveCommand { get; }
    public ICommand CancelDataMoveCommand { get; }
    public ICommand SelectAllDataTablesCommand { get; }
    public ICommand SelectNoDataTablesCommand { get; }
    /// <summary>Supplied by the main window so navigation can open the separate data-sync window.</summary>
    public Action? OpenMoveDataWindowAction { get; set; }
    public Action? OpenBackupWindowAction { get; set; }
    public Action? OpenDiagramWindowAction { get; set; }

    /// <summary>Set by the View to enable clipboard operations from the ViewModel.</summary>
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

    public MainViewModel()
    {
        CompareCommand = new AsyncRelayCommand(CompareAsync, CanCompare);
        GenerateScriptCommand = new AsyncRelayCommand(GenerateScriptAsync, () => HasResults && !IsComparing);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => HasResults && !IsComparing);
        ConfirmApplyCommand = new AsyncRelayCommand(ConfirmApplyAsync);
        CancelApplyCommand = new RelayCommand(CancelApply);
        TestSourceConnectionCommand = new AsyncRelayCommand(() => TestConnectionAsync(GetSourceInfo(), isSource: true));
        TestTargetConnectionCommand = new AsyncRelayCommand(() => TestConnectionAsync(GetTargetInfo(), isSource: false));
        SelectAllCommand = new RelayCommand(SelectAll);
        DeselectAllCommand = new RelayCommand(DeselectAll);
        ExpandAllDiffGroupsCommand = new RelayCommand(() => { foreach (var g in GroupedDifferences) g.IsExpanded = true; });
        CollapseAllDiffGroupsCommand = new RelayCommand(() => { foreach (var g in GroupedDifferences) g.IsExpanded = false; });
        ToggleDiffGroupCommand = new RelayCommand<object?>(param => { if (param is DiffGroup g) g.IsExpanded = !g.IsExpanded; });
        ToggleTargetPaneCommand = new RelayCommand(() => ShowTargetPane = !ShowTargetPane);
        ToggleSourcePaneCommand = new RelayCommand(() => ShowSourcePane = !ShowSourcePane);
        ClearLogsCommand = new RelayCommand(ClearLogs);
        OpenLogsCommand = new RelayCommand(() => ShowLogsDialog = true);
        CloseLogsCommand = new RelayCommand(() => ShowLogsDialog = false);
        SaveSourceProfileCommand = new RelayCommand(() => SaveProfile(isSource: true));
        SaveTargetProfileCommand = new RelayCommand(() => SaveProfile(isSource: false));
        DeleteSavedProfileCommand = new RelayCommand<object?>(DeleteProfile);
        OpenSchemaCompareCommand = new RelayCommand(() => IsMoveDataPage = false);
        OpenMoveDataCommand = new RelayCommand(() => OpenMoveDataWindowAction?.Invoke());
        OpenBackupCommand = new RelayCommand(() => OpenBackupWindowAction?.Invoke());
        OpenDiagramCommand = new RelayCommand(() => OpenDiagramWindowAction?.Invoke());
        ExportBackupCommand = new AsyncRelayCommand(ExportBackupAsync, () => !IsBackingUp && !string.IsNullOrWhiteSpace(BackupDestinationPath));
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarOpen = !IsSidebarOpen);
        CopyErrorCommand = new AsyncRelayCommand(CopyErrorAsync);
        AnalyzeDataMoveCommand = new AsyncRelayCommand(AnalyzeDataMoveAsync, CanCompare);
        StartDataMoveCommand = new RelayCommand(() => ShowDataMoveConfirmation = true, () => HasDataMovePlan && !IsComparing && DataMoveTables.Any(t => t.IsSelected && t.Warning is null));
        ConfirmDataMoveCommand = new AsyncRelayCommand(ConfirmDataMoveAsync);
        CancelDataMoveCommand = new RelayCommand(() => ShowDataMoveConfirmation = false);
        SelectAllDataTablesCommand = new RelayCommand(() => { foreach (var t in DataMoveTables.Where(t => t.Warning is null)) t.IsSelected = true; ((RelayCommand)StartDataMoveCommand).NotifyCanExecuteChanged(); });
        SelectNoDataTablesCommand = new RelayCommand(() => { foreach (var t in DataMoveTables) t.IsSelected = false; ((RelayCommand)StartDataMoveCommand).NotifyCanExecuteChanged(); });

        foreach (var saved in _savedService.Load())
            SavedConnections.Add(saved);

        _isComparingChanged = () =>
        {
            OnPropertyChanged(nameof(IsComparing));
            ((AsyncRelayCommand)CompareCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)GenerateScriptCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ApplyCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)AnalyzeDataMoveCommand).NotifyCanExecuteChanged();
            ((RelayCommand)StartDataMoveCommand).NotifyCanExecuteChanged();
        };
        _hasResultsChanged = () =>
        {
            ((AsyncRelayCommand)GenerateScriptCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ApplyCommand).NotifyCanExecuteChanged();
        };
    }

    private Action? _isComparingChanged;
    partial void OnIsComparingChanged(bool value) => _isComparingChanged?.Invoke();

    private Action? _hasResultsChanged;
    partial void OnHasResultsChanged(bool value) => _hasResultsChanged?.Invoke();

    private bool CanCompare() => !IsComparing;

    partial void OnShowAddedChanged(bool value) => ApplyFilter();
    partial void OnShowChangedChanged(bool value) => ApplyFilter();
    partial void OnShowDeletedChanged(bool value) => ApplyFilter();
    partial void OnSourceUseWindowsAuthChanged(bool value) { OnPropertyChanged(nameof(SourceUseWindowsAuth)); ShowError = false; ClearSourceSelectionOnManualEdit(); }
    partial void OnTargetUseWindowsAuthChanged(bool value) { OnPropertyChanged(nameof(TargetUseWindowsAuth)); ShowError = false; ClearTargetSelectionOnManualEdit(); }

    partial void OnSourceServerChanged(string value) => ClearSourceSelectionOnManualEdit();
    partial void OnSourceDatabaseChanged(string value) => ClearSourceSelectionOnManualEdit();
    partial void OnSourceUsernameChanged(string value) => ClearSourceSelectionOnManualEdit();
    partial void OnSourcePasswordChanged(string value) => ClearSourceSelectionOnManualEdit();
    partial void OnTargetServerChanged(string value) => ClearTargetSelectionOnManualEdit();
    partial void OnTargetDatabaseChanged(string value) => ClearTargetSelectionOnManualEdit();
    partial void OnTargetUsernameChanged(string value) => ClearTargetSelectionOnManualEdit();
    partial void OnTargetPasswordChanged(string value) => ClearTargetSelectionOnManualEdit();

    partial void OnHasDataMovePlanChanged(bool value) => ((RelayCommand)StartDataMoveCommand).NotifyCanExecuteChanged();
    partial void OnDataMoveFilterTextChanged(string value) => ApplyDataMoveFilter();
    partial void OnIsSidebarOpenChanged(bool value) => SidebarWidth = new GridLength(value ? 220 : 0);
    partial void OnBackupDestinationPathChanged(string value) => ((AsyncRelayCommand)ExportBackupCommand).NotifyCanExecuteChanged();
    partial void OnIsBackingUpChanged(bool value) => ((AsyncRelayCommand)ExportBackupCommand).NotifyCanExecuteChanged();

    partial void OnSelectedSavedSourceChanged(SavedConnection? value)
    {
        if (value != null && !_applyingProfile)
            ApplyProfile(value, isSource: true);
    }

    partial void OnSelectedSavedTargetChanged(SavedConnection? value)
    {
        if (value != null && !_applyingProfile)
            ApplyProfile(value, isSource: false);
    }

    private void ClearSourceSelectionOnManualEdit()
    {
        if (_applyingProfile) return;
        InvalidateDataMovePlan();
        if (SelectedSavedSource != null)
            SelectedSavedSource = null;
    }

    private void ClearTargetSelectionOnManualEdit()
    {
        if (_applyingProfile) return;
        InvalidateDataMovePlan();
        if (SelectedSavedTarget != null)
            SelectedSavedTarget = null;
    }

    private void ApplyProfile(SavedConnection profile, bool isSource)
    {
        _applyingProfile = true;
        try
        {
            if (isSource)
            {
                SourceServer = profile.Server;
                SourceDatabase = profile.Database;
                SourceUseWindowsAuth = profile.UseWindowsAuth;
                SourceUsername = profile.Username;
                SourcePassword = profile.Password;
                SourceHasConnectionResult = false;
            }
            else
            {
                TargetServer = profile.Server;
                TargetDatabase = profile.Database;
                TargetUseWindowsAuth = profile.UseWindowsAuth;
                TargetUsername = profile.Username;
                TargetPassword = profile.Password;
                TargetHasConnectionResult = false;
            }
        }
        finally { _applyingProfile = false; }
        InvalidateDataMovePlan();
        ShowError = false;
        var side = isSource ? "Source" : "Target";
        StatusMessage = $"Loaded saved profile '{profile.DisplayName}' into {side}.";
        AppendLog($"{side}: loaded saved profile '{profile.DisplayName}'.");
    }

    private void SaveProfile(bool isSource)
    {
        var server = isSource ? SourceServer : TargetServer;
        var database = isSource ? SourceDatabase : TargetDatabase;
        var useWinAuth = isSource ? SourceUseWindowsAuth : TargetUseWindowsAuth;
        var username = isSource ? SourceUsername : TargetUsername;
        var password = isSource ? SourcePassword : TargetPassword;
        var side = isSource ? "Source" : "Target";

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            ErrorMessage = $"Cannot save {side} profile: server and database are required.";
            ShowError = true;
            return;
        }

        var existing = SavedConnections.FirstOrDefault(c => c.Matches(server, database, useWinAuth, username));
        if (existing != null)
        {
            _applyingProfile = true;
            try
            {
                existing.Server = server.Trim();
                existing.Database = database.Trim();
                existing.UseWindowsAuth = useWinAuth;
                existing.Username = username?.Trim() ?? string.Empty;
                existing.Password = password ?? string.Empty;
            }
            finally { _applyingProfile = false; }
            PersistSavedConnections();
            _applyingProfile = true;
            try
            {
                if (isSource) SelectedSavedSource = existing;
                else SelectedSavedTarget = existing;
            }
            finally { _applyingProfile = false; }
            StatusMessage = $"Updated saved profile '{existing.DisplayName}'.";
            AppendLog($"{side}: updated saved profile '{existing.DisplayName}'.");
        }
        else
        {
            var profile = new SavedConnection
            {
                Server = server.Trim(),
                Database = database.Trim(),
                UseWindowsAuth = useWinAuth,
                Username = username?.Trim() ?? string.Empty,
                Password = password ?? string.Empty,
            };
            SavedConnections.Add(profile);
            PersistSavedConnections();
            _applyingProfile = true;
            try
            {
                if (isSource) SelectedSavedSource = profile;
                else SelectedSavedTarget = profile;
            }
            finally { _applyingProfile = false; }
            StatusMessage = $"Saved {side} credentials as '{profile.DisplayName}'.";
            AppendLog($"{side}: saved new profile '{profile.DisplayName}'.");
        }
        ShowError = false;
    }

    private void DeleteProfile(object? param)
    {
        if (param is not SavedConnection profile)
            return;
        SavedConnections.Remove(profile);
        if (SelectedSavedSource?.Id == profile.Id) SelectedSavedSource = null;
        if (SelectedSavedTarget?.Id == profile.Id) SelectedSavedTarget = null;
        PersistSavedConnections();
        StatusMessage = $"Removed saved profile '{profile.DisplayName}'.";
        AppendLog($"Removed saved profile '{profile.DisplayName}'.");
    }

    private void PersistSavedConnections()
    {
        try { _savedService.Save(SavedConnections); }
        catch (Exception ex) { AppendLog($"WARNING: could not persist saved credentials: {ex.Message}"); }
    }

    partial void OnSelectedDiffChanged(SchemaDiffItem? value)
    {
        if (_previousSelected != null) _previousSelected.IsSelected = false;
        HasSelection = value != null;
        if (value != null)
        {
            value.IsSelected = true;
            DiffScriptContent = BuildDiffDisplay(value);
            SourceScriptContent = value.SourceScript;
            TargetScriptContent = value.TargetScript;
            HasSourceScript = !string.IsNullOrEmpty(value.SourceScript);
            HasTargetScript = !string.IsNullOrEmpty(value.TargetScript);
            SelectedObjectName = value.ObjectName;
            SelectedObjectType = value.ObjectType;
        }
        _previousSelected = value;
    }
    private SchemaDiffItem? _previousSelected;

    private void SelectAll()
    {
        foreach (var item in FilteredDifferences) item.IsIncluded = true;
    }

    private void DeselectAll()
    {
        foreach (var item in FilteredDifferences) item.IsIncluded = false;
    }

    private void ClearLogs()
    {
        LogText = "";
        HasLogs = false;
        LogCount = 0;
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogText = string.IsNullOrEmpty(LogText) ? line : LogText + Environment.NewLine + line;
        HasLogs = true;
        LogCount++;
    }

    public async Task CopyLogsToClipboardAsync()
    {
        if (CopyToClipboardAsync != null && !string.IsNullOrEmpty(LogText))
            await CopyToClipboardAsync(LogText);
    }

    private async Task CopyErrorAsync()
    {
        if (CopyToClipboardAsync != null && !string.IsNullOrEmpty(ErrorMessage))
            await CopyToClipboardAsync(ErrorMessage);
    }

    private void ApplyFilter()
    {
        FilteredDifferences.Clear();
        foreach (var item in Differences)
        {
            if (item.Status == DiffStatus.Added && !ShowAdded) continue;
            if (item.Status == DiffStatus.Changed && !ShowChanged) continue;
            if (item.Status == DiffStatus.Deleted && !ShowDeleted) continue;
            FilteredDifferences.Add(item);
        }
        RebuildDiffGroups();
    }

    private void RebuildDiffGroups()
    {
        // Preserve expand/collapse state across re-filters.
        var expanded = GroupedDifferences.ToDictionary(g => g.ObjectType, g => g.IsExpanded);
        GroupedDifferences.Clear();
        foreach (var grp in FilteredDifferences
                     .GroupBy(i => DiffGroup.NormalizeType(i.ObjectType))
                     .OrderBy(g => g.Key))
        {
            var group = new DiffGroup
            {
                ObjectType = grp.Key,
                Icon = DiffGroup.IconFor(grp.Key),
                IsExpanded = expanded.TryGetValue(grp.Key, out var wasOpen) ? wasOpen : true,
            };
            foreach (var item in grp.OrderBy(i => i.ObjectName))
                group.Items.Add(item);
            group.RefreshCounts();
            GroupedDifferences.Add(group);
        }
    }

    private static string BuildDiffDisplay(SchemaDiffItem item)
    {
        var lines = new List<string> { $"-- Object: {item.ObjectName}", $"-- Type: {item.ObjectType}", $"-- Status: {item.Status}", "" };
        if (!string.IsNullOrEmpty(item.TargetScript)) { lines.Add("-- Target (current):"); lines.Add(item.TargetScript); }
        if (!string.IsNullOrEmpty(item.SourceScript))
        {
            if (!string.IsNullOrEmpty(item.TargetScript)) lines.Add("");
            lines.Add("-- Source (new):"); lines.Add(item.SourceScript);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private async Task TestConnectionAsync(ConnectionInfo info, bool isSource)
    {
        var label = isSource ? "Source" : "Target";
        StatusMessage = $"Testing {label} connection...";
        ShowError = false;
        if (isSource) { SourceConnectionStatus = "Testing..."; SourceConnectionOk = false; SourceHasConnectionResult = true; }
        else          { TargetConnectionStatus = "Testing..."; TargetConnectionOk = false; TargetHasConnectionResult = true; }
        try
        {
            var msg = await _compareService.TestConnectionAsync(info);
            StatusMessage = $"{label}: {msg}";
            AppendLog($"{label} connection: {msg}");
            if (isSource) { SourceConnectionStatus = msg; SourceConnectionOk = true; }
            else          { TargetConnectionStatus = msg; TargetConnectionOk = true; }
        }
        catch (Exception ex)
        {
            var errMsg = ex.Message;
            ErrorMessage = errMsg; ShowError = true;
            StatusMessage = $"{label} connection failed";
            AppendLog($"{label} connection FAILED: {errMsg}");
            if (isSource) { SourceConnectionStatus = $"Failed: {errMsg}"; SourceConnectionOk = false; }
            else          { TargetConnectionStatus = $"Failed: {errMsg}"; TargetConnectionOk = false; }
        }
    }

    private async Task CompareAsync()
    {
        var sourceInfo = GetSourceInfo();
        var targetInfo = GetTargetInfo();
        if (string.IsNullOrWhiteSpace(sourceInfo.Server) || string.IsNullOrWhiteSpace(targetInfo.Server))
        { ShowError = true; ErrorMessage = "Please enter both source and target server addresses."; StatusMessage = ErrorMessage; return; }
        if (string.IsNullOrWhiteSpace(sourceInfo.Database) || string.IsNullOrWhiteSpace(targetInfo.Database))
        { ShowError = true; ErrorMessage = "Please enter both source and target database names."; StatusMessage = ErrorMessage; return; }

        IsComparing = true; ProgressValue = 0; Differences.Clear(); FilteredDifferences.Clear();
        HasResults = false; ShowError = false; ErrorMessage = "";
        FullDeployScript = ""; HasFullScript = false; SelectedDiff = null;
        HasSourceScript = false; HasTargetScript = false;
        StatusMessage = "Comparing...";
        AppendLog($"--- Compare started: {sourceInfo.Server}/{sourceInfo.Database} -> {targetInfo.Server}/{targetInfo.Database} ---");
        try
        {
            var p = new Progress<string>(m =>
            {
                ProgressText = m;
                StatusMessage = m;
                AppendLog(m);
            });
            var (items, summary) = await _compareService.CompareAsync(sourceInfo, targetInfo, p);
            TotalAdded = summary.AddedCount; TotalChanged = summary.ChangedCount; TotalDeleted = summary.DeletedCount;
            AddedFilterText = $"Added: {summary.AddedCount}"; ChangedFilterText = $"Changed: {summary.ChangedCount}"; DeletedFilterText = $"Deleted: {summary.DeletedCount}";
            foreach (var item in items) Differences.Add(item);
            ApplyFilter(); HasResults = true;
            ResultSummaryText = summary.TotalDifferences > 0
                ? $"{summary.TotalDifferences} differences found (+{summary.AddedCount} / ~{summary.ChangedCount} / -{summary.DeletedCount})"
                : "No differences found. Databases are identical.";
            StatusMessage = ResultSummaryText;
            AppendLog($"--- Compare finished: {ResultSummaryText} ---");
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; ErrorMessage = ex.Message; ShowError = true; AppendLog($"ERROR: {ex.Message}"); }
        finally { IsComparing = false; ProgressValue = 100; }
    }

    private async Task GenerateScriptAsync()
    {
        if (!HasResults) return;
        IsComparing = true; StatusMessage = "Generating deployment script...";
        AppendLog("Generating deployment script...");
        try
        {
            FullDeployScript = await _compareService.GenerateScriptAsync(GetSourceInfo(), GetTargetInfo());
            HasFullScript = true; StatusMessage = "Deployment script generated.";
            AppendLog("Deployment script generated successfully.");
        }
        catch (Exception ex) { StatusMessage = $"Error generating script: {ex.Message}"; AppendLog($"Script generation ERROR: {ex.Message}"); }
        finally { IsComparing = false; }
    }

    private void CancelApply() => ShowApplyConfirmation = false;

    private async Task ConfirmApplyAsync()
    {
        ShowApplyConfirmation = false;
        await ApplyAsync();
    }

    private async Task ApplyAsync()
    {
        if (!HasResults) return;

        // Only apply items the user has checked
        var includedItems = Differences.Where(d => d.IsIncluded).ToList();
        if (includedItems.Count == 0)
        {
            StatusMessage = "No items selected for apply. Check at least one item in the differences list.";
            ErrorMessage = StatusMessage; ShowError = true;
            return;
        }

        IsComparing = true; StatusMessage = $"Applying {includedItems.Count} selected changes...";
        AppendLog($"--- Apply started: {includedItems.Count} item(s) selected ---");
        try
        {
            var p = new Progress<string>(m => { ProgressText = m; StatusMessage = m; AppendLog(m); });
            var (_, script) = await _compareService.ApplyChangesAsync(GetSourceInfo(), GetTargetInfo(), p, includedItems);
            FullDeployScript = script; HasFullScript = true;
            StatusMessage = "Changes applied successfully.";
            AppendLog("--- Apply finished successfully ---");
        }
        catch (Exception ex) { StatusMessage = $"Error applying changes: {ex.Message}"; AppendLog($"Apply ERROR: {ex.Message}"); }
        finally { IsComparing = false; }
    }

    private void InvalidateDataMovePlan()
    {
        if (!HasDataMovePlan) return;
        _dataMovePlan = null;
        DataMoveTables.Clear();
        FilteredDataMoveTables.Clear();
        HasDataMovePlan = false;
        ShowDataMoveConfirmation = false;
        DataMoveSummary = "Connections changed. Analyze tables and relations again before data sync.";
    }

    private async Task AnalyzeDataMoveAsync()
    {
        var source = GetSourceInfo();
        var target = GetTargetInfo();
        if (string.IsNullOrWhiteSpace(source.Server) || string.IsNullOrWhiteSpace(source.Database) ||
            string.IsNullOrWhiteSpace(target.Server) || string.IsNullOrWhiteSpace(target.Database))
        {
            ErrorMessage = "Enter both Source and Target server/database values before analyzing data move.";
            ShowError = true; StatusMessage = ErrorMessage; return;
        }

        IsComparing = true; ShowError = false; HasDataMovePlan = false; DataMoveTables.Clear();
        AppendLog($"--- Data move analysis: {source.Server}/{source.Database} -> {target.Server}/{target.Database} ---");
        try
        {
            var p = new Progress<string>(m => { StatusMessage = m; ProgressText = m; AppendLog("Data move: " + m); });
            _dataMovePlan = await _dataMoveService.AnalyzeAsync(source, target, p);
            foreach (var table in _dataMovePlan.Tables) DataMoveTables.Add(table);
            ApplyDataMoveFilter();
            var usable = DataMoveTables.Count(t => t.Warning is null);
            var blocked = DataMoveTables.Count - usable;
            var relationCount = _dataMovePlan.ParentTables.Sum(x => x.Value.Count);
            DataMoveSummary = $"{usable} compatible table(s) ready; {blocked} blocked. Select exactly what to synchronize.";
            DataMoveRelationshipSummary = $"Found {relationCount} foreign-key relationship(s). Selected parent tables will be inserted/updated before their children.";
            HasDataMovePlan = true; StatusMessage = "Data move analysis complete.";
            AppendLog($"--- Data move analysis complete: {usable} usable, {blocked} blocked, {relationCount} FK links ---");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message; ShowError = true; StatusMessage = "Data move analysis failed.";
            AppendLog($"Data move analysis ERROR: {ex.Message}");
        }
        finally { IsComparing = false; }
    }

    private async Task ConfirmDataMoveAsync()
    {
        ShowDataMoveConfirmation = false;
        if (_dataMovePlan is null) return;
        List<DataMoveTable> selected;
        try
        {
            selected = IncludeRequiredParentTables();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message; ShowError = true; StatusMessage = "Data synchronization cannot start.";
            AppendLog($"Data synchronization preflight ERROR: {ex.Message}");
            return;
        }
        if (selected.Count == 0) { ErrorMessage = "Select at least one compatible table."; ShowError = true; return; }

        IsComparing = true; ShowError = false;
        AppendLog($"--- Data synchronization started: {selected.Count} selected table(s); target rows are never deleted ---");
        try
        {
            var p = new Progress<string>(m => { StatusMessage = m; ProgressText = m; AppendLog("Data move: " + m); });
            var result = await _dataMoveService.SyncAsync(GetSourceInfo(), GetTargetInfo(), selected, _dataMovePlan, p);
            DataMoveSummary = $"Completed {result.TablesCompleted} table(s): {result.InsertedRows:N0} inserted, {result.UpdatedRows:N0} updated. No target rows were deleted.";
            StatusMessage = "Data synchronization completed.";
            AppendLog($"--- Data synchronization completed: {result.TablesCompleted} tables, {result.InsertedRows} inserted, {result.UpdatedRows} updated ---");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message; ShowError = true; StatusMessage = "Data synchronization stopped.";
            AppendLog($"Data synchronization ERROR: {ex.Message}");
        }
        finally { IsComparing = false; }
    }

    /// <summary>
    /// A child row cannot be inserted before its referenced parent exists on the target.
    /// Expand the user's selection transitively, so choosing Orders also brings in Users,
    /// then any parents of Users. A non-copyable parent blocks the operation before writes.
    /// </summary>
    private List<DataMoveTable> IncludeRequiredParentTables()
    {
        if (_dataMovePlan is null) return [];
        var all = DataMoveTables.ToDictionary(t => $"{t.Schema}.{t.Name}", StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<string>(DataMoveTables.Where(t => t.IsSelected).Select(t => $"{t.Schema}.{t.Name}"), StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(selected);
        var added = new List<string>();

        while (pending.TryDequeue(out var child))
        {
            if (!_dataMovePlan.ParentTables.TryGetValue(child, out var parentKeys)) continue;
            foreach (var parent in parentKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!all.TryGetValue(parent, out var table))
                    throw new InvalidOperationException($"'{child}' references '{parent}', but that parent table is unavailable for copying.");
                if (table.Warning is not null)
                    throw new InvalidOperationException($"'{child}' requires parent '{parent}', but it is blocked: {table.Warning}.");
                if (selected.Add(parent))
                {
                    table.IsSelected = true;
                    added.Add(parent);
                    pending.Enqueue(parent);
                }
            }
        }

        if (added.Count > 0)
            AppendLog("Data move: automatically included required parent table(s): " + string.Join(", ", added));
        return selected.Select(key => all[key]).ToList();
    }

    private void ApplyDataMoveFilter()
    {
        FilteredDataMoveTables.Clear();
        var filter = DataMoveFilterText?.Trim() ?? string.Empty;
        foreach (var table in DataMoveTables)
            if (string.IsNullOrEmpty(filter) || table.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase) || table.StatusText.Contains(filter, StringComparison.OrdinalIgnoreCase))
                FilteredDataMoveTables.Add(table);
    }

    public string BackupSuggestedFileName => $"{(BackupUsesSource ? SourceDatabase : TargetDatabase)}_{DateTime.Now:yyyyMMdd_HHmmss}.bacpac";

    private async Task ExportBackupAsync()
    {
        var database = BackupUsesSource ? GetSourceInfo() : GetTargetInfo();
        if (string.IsNullOrWhiteSpace(database.Server) || string.IsNullOrWhiteSpace(database.Database))
        { ErrorMessage = "Choose a valid database connection before exporting a backup."; ShowError = true; return; }
        var path = BackupDestinationPath.Trim();
        if (!path.EndsWith(".bacpac", StringComparison.OrdinalIgnoreCase)) path += ".bacpac";
        IsBackingUp = true; ShowError = false; BackupStatus = "Starting local BACPAC export...";
        AppendLog($"--- BACPAC export started: {database.Server}/{database.Database} -> {path} ---");
        try
        {
            var progress = new Progress<string>(m => { BackupStatus = m; AppendLog("Backup: " + m); });
            var result = await _backupService.ExportBacpacAsync(database, path, progress);
            BackupDestinationPath = path; BackupStatus = result;
            AppendLog($"--- BACPAC export completed: {result} ---");
        }
        catch (Exception ex) { ErrorMessage = ex.Message; ShowError = true; BackupStatus = "Backup failed."; AppendLog($"BACPAC export ERROR: {ex.Message}"); }
        finally { IsBackingUp = false; }
    }

    private ConnectionInfo GetSourceInfo() => new()
    {
        Server = SourceServer, Database = SourceDatabase, UseWindowsAuth = SourceUseWindowsAuth,
        Username = SourceUsername, Password = SourcePassword
    };

    private ConnectionInfo GetTargetInfo() => new()
    {
        Server = TargetServer, Database = TargetDatabase, UseWindowsAuth = TargetUseWindowsAuth,
        Username = TargetUsername, Password = TargetPassword
    };
}
