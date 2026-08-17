using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SchemaCompareService _compareService = new();

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

    public ObservableCollection<SchemaDiffItem> Differences { get; } = [];
    public ObservableCollection<SchemaDiffItem> FilteredDifferences { get; } = [];

    [ObservableProperty] private bool _showAdded = true;
    [ObservableProperty] private bool _showChanged = true;
    [ObservableProperty] private bool _showDeleted = true;

    public ICommand CompareCommand { get; }
    public ICommand GenerateScriptCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand ConfirmApplyCommand { get; }
    public ICommand CancelApplyCommand { get; }
    public ICommand TestSourceConnectionCommand { get; }
    public ICommand TestTargetConnectionCommand { get; }

    public MainViewModel()
    {
        CompareCommand = new AsyncRelayCommand(CompareAsync, CanCompare);
        GenerateScriptCommand = new AsyncRelayCommand(GenerateScriptAsync, () => HasResults && !IsComparing);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => HasResults && !IsComparing);
        ConfirmApplyCommand = new AsyncRelayCommand(ConfirmApplyAsync);
        CancelApplyCommand = new RelayCommand(CancelApply);
        TestSourceConnectionCommand = new AsyncRelayCommand(() => TestConnectionAsync(GetSourceInfo(), isSource: true));
        TestTargetConnectionCommand = new AsyncRelayCommand(() => TestConnectionAsync(GetTargetInfo(), isSource: false));

        _isComparingChanged = () =>
        {
            OnPropertyChanged(nameof(IsComparing));
            ((AsyncRelayCommand)CompareCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)GenerateScriptCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ApplyCommand).NotifyCanExecuteChanged();
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
    partial void OnSourceUseWindowsAuthChanged(bool value) { OnPropertyChanged(nameof(SourceUseWindowsAuth)); ShowError = false; }
    partial void OnTargetUseWindowsAuthChanged(bool value) { OnPropertyChanged(nameof(TargetUseWindowsAuth)); ShowError = false; }

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
        // Don't set IsComparing=true here — it disables the Compare button and
        // shows the loading overlay. Use a lightweight status update instead.
        ShowError = false;
        if (isSource) { SourceConnectionStatus = "Testing..."; SourceConnectionOk = false; SourceHasConnectionResult = true; }
        else          { TargetConnectionStatus = "Testing..."; TargetConnectionOk = false; TargetHasConnectionResult = true; }
        try
        {
            var msg = await _compareService.TestConnectionAsync(info);
            StatusMessage = $"{label}: {msg}";
            if (isSource) { SourceConnectionStatus = msg; SourceConnectionOk = true; }
            else          { TargetConnectionStatus = msg; TargetConnectionOk = true; }
        }
        catch (Exception ex)
        {
            var errMsg = ex.Message;
            ErrorMessage = errMsg; ShowError = true;
            StatusMessage = $"{label} connection failed";
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
        try
        {
            var p = new Progress<string>(m => { ProgressText = m; StatusMessage = m; });
            var (items, summary) = await _compareService.CompareAsync(sourceInfo, targetInfo, p);
            TotalAdded = summary.AddedCount; TotalChanged = summary.ChangedCount; TotalDeleted = summary.DeletedCount;
            AddedFilterText = $"Added: {summary.AddedCount}"; ChangedFilterText = $"Changed: {summary.ChangedCount}"; DeletedFilterText = $"Deleted: {summary.DeletedCount}";
            foreach (var item in items) Differences.Add(item);
            ApplyFilter(); HasResults = true;
            ResultSummaryText = summary.TotalDifferences > 0
                ? $"{summary.TotalDifferences} differences found (+{summary.AddedCount} / ~{summary.ChangedCount} / -{summary.DeletedCount})"
                : "No differences found. Databases are identical.";
            StatusMessage = ResultSummaryText;
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; ErrorMessage = ex.Message; ShowError = true; }
        finally { IsComparing = false; ProgressValue = 100; }
    }

    private async Task GenerateScriptAsync()
    {
        if (!HasResults) return;
        IsComparing = true; StatusMessage = "Generating deployment script...";
        try
        {
            FullDeployScript = await _compareService.GenerateScriptAsync(GetSourceInfo(), GetTargetInfo());
            HasFullScript = true; StatusMessage = "Deployment script generated.";
        }
        catch (Exception ex) { StatusMessage = $"Error generating script: {ex.Message}"; }
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
        IsComparing = true; StatusMessage = "Applying changes...";
        try
        {
            var p = new Progress<string>(m => { ProgressText = m; StatusMessage = m; });
            var (_, script) = await _compareService.ApplyChangesAsync(GetSourceInfo(), GetTargetInfo(), p);
            FullDeployScript = script; HasFullScript = true; StatusMessage = "Changes applied successfully.";
        }
        catch (Exception ex) { StatusMessage = $"Error applying changes: {ex.Message}"; }
        finally { IsComparing = false; }
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
