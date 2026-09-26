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
    private readonly SchemaSnapshotService _snapshotService = new();
    private readonly DataMoveService _dataMoveService = new();
    private readonly DatabaseBackupService _backupService = new();
    private readonly SavedConnectionsService _savedService = new();
    private readonly SnapshotLibraryService _snapshotLibrary = new();
    private readonly SavedComparisonsService _savedComparisons = new();
    private readonly AppSettingsService _settingsService = new();
    private bool _applyingProfile;
    private bool _syncingSnapshotRow;
    private bool _applyingSettings;

    [ObservableProperty] private string _sourceServer = "";
    [ObservableProperty] private string _sourceDatabase = "";
    [ObservableProperty] private AuthMethod _sourceAuth = AuthMethod.Windows;
    [ObservableProperty] private bool _sourceUseWindowsAuth = true;
    [ObservableProperty] private string _sourceUsername = "";
    [ObservableProperty] private string _sourcePassword = "";

    [ObservableProperty] private string _targetServer = "";
    [ObservableProperty] private string _targetDatabase = "";
    [ObservableProperty] private AuthMethod _targetAuth = AuthMethod.Windows;
    [ObservableProperty] private bool _targetUseWindowsAuth = true;
    [ObservableProperty] private string _targetUsername = "";
    [ObservableProperty] private string _targetPassword = "";

    /// <summary>The authentication dropdown contents — Windows, SQL and the Entra ID flows.</summary>
    public IReadOnlyList<AuthMethod> AuthMethods { get; } = AuthMethod.All;

    // Per-connection TLS posture (see ConnectionInfo). TrustServerCertificate stays
    // on by default so local instances without a trusted cert keep working.
    [ObservableProperty] private bool _sourceEncryptConnection;
    [ObservableProperty] private bool _sourceTrustServerCertificate = true;
    [ObservableProperty] private bool _targetEncryptConnection;
    [ObservableProperty] private bool _targetTrustServerCertificate = true;

    // Schema snapshots: either side of the comparison can be a .dacpac captured earlier
    // instead of a live database, which is how a baseline outlives the server it came from.
    [ObservableProperty] private bool _sourceIsSnapshot;
    [ObservableProperty] private string _sourceSnapshotPath = "";
    [ObservableProperty] private string _sourceSnapshotCaption = "";
    [ObservableProperty] private bool _targetIsSnapshot;
    [ObservableProperty] private string _targetSnapshotPath = "";
    [ObservableProperty] private string _targetSnapshotCaption = "";
    [ObservableProperty] private bool _isCapturingSnapshot;

    // The baselines this app has captured or opened before, so "compare against yesterday's
    // snapshot" is a pick from a list instead of a file dialog into a folder of timestamps.
    public ObservableCollection<SnapshotEntry> SnapshotLibrary => _snapshotLibrary.Entries;
    public bool HasSnapshots => SnapshotLibrary.Count > 0;

    [ObservableProperty] private SnapshotEntry? _selectedSourceSnapshot;
    [ObservableProperty] private SnapshotEntry? _selectedTargetSnapshot;

    // A pairing worth running again — the weekly check between one database and one baseline —
    // kept as references to the two sides rather than as a copy of their credentials.
    public ObservableCollection<SavedComparison> SavedComparisons => _savedComparisons.Entries;
    public bool HasSavedComparisons => SavedComparisons.Count > 0;
    [ObservableProperty] private SavedComparison? _selectedSavedComparison;
    [ObservableProperty] private string _comparisonName = "";

    /// <summary>Capturing needs a live database on that side, and re-capturing the side you are
    /// already reading from a file would only overwrite the baseline.</summary>
    public bool CanCaptureSourceSnapshot =>
        !IsCapturingSnapshot && !SourceIsSnapshot && HasLiveSourceEndpoint;
    public bool CanCaptureTargetSnapshot =>
        !IsCapturingSnapshot && !TargetIsSnapshot && HasLiveTargetEndpoint;

    // The card headers say which of the two things that side currently is.
    public string SourceCardTitle => SourceIsSnapshot ? "SOURCE SNAPSHOT" : "SOURCE DATABASE";
    public string TargetCardTitle => TargetIsSnapshot ? "TARGET SNAPSHOT" : "TARGET DATABASE";

    private bool HasLiveSourceEndpoint =>
        !string.IsNullOrWhiteSpace(SourceServer) && !string.IsNullOrWhiteSpace(SourceDatabase);
    private bool HasLiveTargetEndpoint =>
        !string.IsNullOrWhiteSpace(TargetServer) && !string.IsNullOrWhiteSpace(TargetDatabase);

    private void RefreshSnapshotCapabilities()
    {
        OnPropertyChanged(nameof(CanCaptureSourceSnapshot));
        OnPropertyChanged(nameof(CanCaptureTargetSnapshot));
        RefreshApplyCanExecute();
    }

    private void RefreshApplyCanExecute() => ((AsyncRelayCommand)ApplyCommand).NotifyCanExecuteChanged();

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

    // Display / text settings (persisted, applied to Application.Resources)
    [ObservableProperty] private double _uiScale = AppSettings.DefaultUiScale;
    [ObservableProperty] private double _codeFontSize = AppSettings.DefaultCodeFontSize;
    [ObservableProperty] private bool _showSettingsDialog;

    /// <summary>True when both Target and Source scripts exist — side-by-side layout.</summary>
    public bool HasBothScripts => HasSourceScript && HasTargetScript;

    /// <summary>True when exactly one script exists — single panel fills full width/height.</summary>
    public bool HasSingleScript => HasSelection && (HasSourceScript ^ HasTargetScript);

    public bool HasOnlySource => HasSelection && HasSourceScript && !HasTargetScript;
    public bool HasOnlyTarget => HasSelection && !HasSourceScript && HasTargetScript;

    public string SingleScriptContent => HasSourceScript ? SourceScriptContent : TargetScriptContent;
    public string SingleScriptHeader => HasSourceScript ? "Source (New)" : "Target (Current)";
    public string DisplaySettingsSummary => $"Text {UiScale:P0} • Code {CodeFontSize:0.#}pt";

    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _resultSummaryText = "";
    [ObservableProperty] private SchemaDiffItem? _selectedDiff;
    [ObservableProperty] private string _diffScriptContent = "";
    [ObservableProperty] private string _sourceScriptContent = "";
    [ObservableProperty] private string _targetScriptContent = "";
    [ObservableProperty] private string _selectedObjectName = "";
    [ObservableProperty] private string _selectedObjectType = "";
    [ObservableProperty] private string _fullDeployScript = "";
    [ObservableProperty] private string _rollbackScript = "";
    [ObservableProperty] private bool _showingRollbackScript;

    /// <summary>What the script pane shows and what Copy puts on the clipboard — never both at once.</summary>
    public string DisplayedScript => ShowingRollbackScript ? RollbackScript : FullDeployScript;
    public string ScriptPaneTitle => ShowingRollbackScript ? "Rollback Script (DOWN)" : "Deployment Script";
    public string ScriptDirectionText => ShowingRollbackScript ? "Show deployment (UP)" : "Show rollback (DOWN)";
    public bool HasRollbackScript => RollbackScript.Length > 0;

    /// <summary>Either direction is enough to open the pane — DOWN is often the only one wanted.</summary>
    public bool HasScriptPane => HasFullScript || HasRollbackScript;

    partial void OnRollbackScriptChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayedScript));
        OnPropertyChanged(nameof(HasRollbackScript));
        OnPropertyChanged(nameof(HasScriptPane));
    }

    partial void OnHasFullScriptChanged(bool value) => OnPropertyChanged(nameof(HasScriptPane));

    partial void OnFullDeployScriptChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayedScript));
    }

    partial void OnShowingRollbackScriptChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayedScript));
        OnPropertyChanged(nameof(ScriptPaneTitle));
        OnPropertyChanged(nameof(ScriptDirectionText));
    }

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
    public ObservableCollection<DataMoveTable> DataMoveTables { get; } = [];
    public ObservableCollection<DataMoveTable> FilteredDataMoveTables { get; } = [];
    private DataMovePlan? _dataMovePlan;

    [ObservableProperty] private bool _showAdded = true;
    [ObservableProperty] private bool _showChanged = true;
    [ObservableProperty] private bool _showDeleted = true;
    [ObservableProperty] private bool _allowUnsafeDrops;
    [ObservableProperty] private bool _allowUnsafeChanges;

    public ICommand CompareCommand { get; }
    public ICommand GenerateScriptCommand { get; }
    public ICommand GenerateRollbackScriptCommand { get; }
    public ICommand CaptureSourceSnapshotCommand { get; }
    public ICommand CaptureTargetSnapshotCommand { get; }
    public ICommand BrowseSourceSnapshotCommand { get; }
    public ICommand BrowseTargetSnapshotCommand { get; }
    public ICommand ForgetSourceSnapshotCommand { get; }
    public ICommand ForgetTargetSnapshotCommand { get; }
    public ICommand SaveComparisonCommand { get; }
    public ICommand ForgetComparisonCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand ConfirmApplyCommand { get; }
    public ICommand CancelApplyCommand { get; }
    public ICommand TestSourceConnectionCommand { get; }
    public ICommand TestTargetConnectionCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand CloseLogsCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand ResetTextSettingsCommand { get; }
    public ICommand SaveSourceProfileCommand { get; }
    public ICommand SaveTargetProfileCommand { get; }
    public ICommand DeleteSavedProfileCommand { get; }
    public ICommand OpenSchemaCompareCommand { get; }
    public ICommand OpenMoveDataCommand { get; }
    public ICommand OpenDataCompareCommand { get; }
    public ICommand OpenImportWizardCommand { get; }
    public ICommand OpenBackupCommand { get; }
    public ICommand OpenDiagramCommand { get; }
    public ICommand OpenDbManagerCommand { get; }
    public ICommand OpenQueryCommand { get; }
    public ICommand OpenDbHealthCommand { get; }
    public ICommand OpenAboutCommand { get; }
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
    /// <summary>Supplied by the main window so navigation can open the row-level data compare window.</summary>
    public Action? OpenDataCompareWindowAction { get; set; }
    /// <summary>Supplied by the main window so navigation can open the CSV import wizard.</summary>
    public Action? OpenImportWindowAction { get; set; }
    public Action? OpenBackupWindowAction { get; set; }
    public Action? OpenDiagramWindowAction { get; set; }
    public Action? OpenDbManagerWindowAction { get; set; }
    public Action? OpenQueryWindowAction { get; set; }
    public Action? OpenDbHealthWindowAction { get; set; }
    public Action? OpenAboutWindowAction { get; set; }

    /// <summary>Version line shown under the header title and in the About window.</summary>
    public string AppVersionText => $"v{AppInfo.VersionText}";

    /// <summary>Set by the View to enable clipboard operations from the ViewModel.</summary>
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

    /// <summary>Set by the view — asks for an existing snapshot file to compare against.
    /// Null when the user cancelled or the host has no file picker.</summary>
    public Func<Task<string?>>? PickSnapshotFileAsync { get; set; }

    /// <summary>Set by the view — asks where to write a captured snapshot, given the name this
    /// class suggests. Null when the user cancelled or no picker is available.</summary>
    public Func<string, Task<string?>>? PickSnapshotSavePathAsync { get; set; }

    public MainViewModel()
    {
        CompareCommand = new AsyncRelayCommand(CompareAsync, CanCompare);
        GenerateScriptCommand = new AsyncRelayCommand(GenerateScriptAsync, () => HasResults && !IsComparing);
        GenerateRollbackScriptCommand = new AsyncRelayCommand(GenerateRollbackScriptAsync, () => HasResults && !IsComparing);
        CaptureSourceSnapshotCommand = new AsyncRelayCommand(() => CaptureSnapshotAsync(isSource: true), () => CanCaptureSourceSnapshot);
        CaptureTargetSnapshotCommand = new AsyncRelayCommand(() => CaptureSnapshotAsync(isSource: false), () => CanCaptureTargetSnapshot);
        BrowseSourceSnapshotCommand = new AsyncRelayCommand(() => BrowseSnapshotAsync(isSource: true), () => !IsCapturingSnapshot);
        BrowseTargetSnapshotCommand = new AsyncRelayCommand(() => BrowseSnapshotAsync(isSource: false), () => !IsCapturingSnapshot);
        ForgetSourceSnapshotCommand = new RelayCommand(() => ForgetSnapshot(isSource: true));
        ForgetTargetSnapshotCommand = new RelayCommand(() => ForgetSnapshot(isSource: false));
        SaveComparisonCommand = new RelayCommand(SaveComparison);
        ForgetComparisonCommand = new RelayCommand(ForgetComparison);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, CanApply);
        ConfirmApplyCommand = new AsyncRelayCommand(ConfirmApplyAsync);
        CancelApplyCommand = new RelayCommand(CancelApply);
        TestSourceConnectionCommand = new AsyncRelayCommand(() => TestConnectionAsync(GetSourceInfo(), isSource: true));
        TestTargetConnectionCommand = new AsyncRelayCommand(() => TestConnectionAsync(GetTargetInfo(), isSource: false));
        SelectAllCommand = new RelayCommand(SelectAll);
        DeselectAllCommand = new RelayCommand(DeselectAll);
        ClearLogsCommand = new RelayCommand(ClearLogs);
        OpenLogsCommand = new RelayCommand(() => ShowLogsDialog = true);
        CloseLogsCommand = new RelayCommand(() => ShowLogsDialog = false);
        OpenSettingsCommand = new RelayCommand(() => ShowSettingsDialog = true);
        CloseSettingsCommand = new RelayCommand(() => ShowSettingsDialog = false);
        ResetTextSettingsCommand = new RelayCommand(ResetTextSettings);
        SaveSourceProfileCommand = new RelayCommand(() => SaveProfile(isSource: true));
        SaveTargetProfileCommand = new RelayCommand(() => SaveProfile(isSource: false));
        DeleteSavedProfileCommand = new RelayCommand<object?>(DeleteProfile);
        OpenSchemaCompareCommand = new RelayCommand(() => IsMoveDataPage = false);
        OpenMoveDataCommand = new RelayCommand(() => OpenMoveDataWindowAction?.Invoke());
        OpenDataCompareCommand = new RelayCommand(() => OpenDataCompareWindowAction?.Invoke());
        OpenImportWizardCommand = new RelayCommand(() => OpenImportWindowAction?.Invoke());
        OpenBackupCommand = new RelayCommand(() => OpenBackupWindowAction?.Invoke());
        OpenDiagramCommand = new RelayCommand(() => OpenDiagramWindowAction?.Invoke());
        OpenDbManagerCommand = new RelayCommand(() => OpenDbManagerWindowAction?.Invoke());
        OpenQueryCommand = new RelayCommand(() => OpenQueryWindowAction?.Invoke());
        OpenDbHealthCommand = new RelayCommand(() => OpenDbHealthWindowAction?.Invoke());
        OpenAboutCommand = new RelayCommand(() => OpenAboutWindowAction?.Invoke());
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

        _snapshotLibrary.Load();
        _snapshotLibrary.Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSnapshots));
        _savedComparisons.Load();
        _savedComparisons.Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSavedComparisons));

        LoadDisplaySettings();

        _isComparingChanged = () =>
        {
            OnPropertyChanged(nameof(IsComparing));
            ((AsyncRelayCommand)CompareCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)GenerateScriptCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)GenerateRollbackScriptCommand).NotifyCanExecuteChanged();
            RefreshApplyCanExecute();
            RefreshSnapshotCommands();
            ((AsyncRelayCommand)AnalyzeDataMoveCommand).NotifyCanExecuteChanged();
            ((RelayCommand)StartDataMoveCommand).NotifyCanExecuteChanged();
        };
        _hasResultsChanged = () =>
        {
            ((AsyncRelayCommand)GenerateScriptCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)GenerateRollbackScriptCommand).NotifyCanExecuteChanged();
            RefreshApplyCanExecute();
        };
    }

    private Action? _isComparingChanged;
    partial void OnIsComparingChanged(bool value) => _isComparingChanged?.Invoke();

    private Action? _hasResultsChanged;
    partial void OnHasResultsChanged(bool value) => _hasResultsChanged?.Invoke();

    private bool CanCompare() => !IsComparing;

    /// <summary>Apply runs batches against a live database, so a snapshot on the target side has
    /// nothing to run against — generating a script from it is still fine.</summary>
    private bool CanApply() => HasResults && !IsComparing && !TargetIsSnapshot;

    private void RefreshSnapshotCommands()
    {
        OnPropertyChanged(nameof(CanCaptureSourceSnapshot));
        OnPropertyChanged(nameof(CanCaptureTargetSnapshot));
        ((AsyncRelayCommand)CaptureSourceSnapshotCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)CaptureTargetSnapshotCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)BrowseSourceSnapshotCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)BrowseTargetSnapshotCommand).NotifyCanExecuteChanged();
        RefreshApplyCanExecute();
    }

    partial void OnSourceSnapshotPathChanged(string value) => OnSnapshotPathChanged(isSource: true, value);
    partial void OnTargetSnapshotPathChanged(string value) => OnSnapshotPathChanged(isSource: false, value);

    /// <summary>
    /// The caption is what tells the operator whether the file beside the box is the baseline
    /// they think it is — a two-year-old snapshot compared without a second look is how a
    /// quiet revert gets deployed — so a path that cannot be read says so instead of going blank.
    /// </summary>
    private void OnSnapshotPathChanged(bool isSource, string value)
    {
        var caption = string.IsNullOrWhiteSpace(value) ? "" : DescribeSnapshot(value);
        if (isSource) SourceSnapshotCaption = caption; else TargetSnapshotCaption = caption;
        // Show the card's row for whatever file is in the box, including one typed by hand or
        // written by a capture, so the list and the card never disagree about the current pick.
        // That is a mirror, not a choice: the flag keeps it from dragging the side into file mode.
        _syncingSnapshotRow = true;
        try
        {
            if (isSource) SelectedSourceSnapshot = _snapshotLibrary.Find(value);
            else SelectedTargetSnapshot = _snapshotLibrary.Find(value);
        }
        finally { _syncingSnapshotRow = false; }
        RefreshSnapshotCommands();
    }

    private string DescribeSnapshot(string path)
    {
        try
        {
            var info = SchemaSnapshotService.ReadSnapshot(path);
            // Every route into this box — capture, Browse, a pick from the list — passes here,
            // so remembering the baseline from one place records it from all three.
            _snapshotLibrary.Remember(info);
            var age = info.AgeCaption.Length > 0 ? $" {info.AgeCaption}." : "";
            return $"{info.Caption}{age} ({info.FileSizeBytes / 1024} KB)";
        }
        catch (Exception ex)
        {
            AppendLog($"Snapshot header unreadable: {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>The library is the shortcut to a file, not a second copy of it: choosing a row
    /// switches that side to snapshot mode and fills the path, and the path then re-syncs the
    /// selection through <see cref="OnSnapshotPathChanged"/>. The two directions need different
    /// rules: a pick must set the mode even when the box already names that file (a capture leaves
    /// the path there with the side still live, and a pick that only wrote the path would leave the
    /// card comparing the database while appearing to read the baseline just chosen), while the
    /// path's own re-sync must not — that is what keeps capturing from flipping the side it
    /// captured from.</summary>
    partial void OnSelectedSourceSnapshotChanged(SnapshotEntry? value)
    {
        if (value is null || _syncingSnapshotRow) return;
        SourceIsSnapshot = true;
        if (!string.Equals(value.Path, SourceSnapshotPath, StringComparison.OrdinalIgnoreCase))
            SourceSnapshotPath = value.Path;
    }

    partial void OnSelectedTargetSnapshotChanged(SnapshotEntry? value)
    {
        if (value is null || _syncingSnapshotRow) return;
        TargetIsSnapshot = true;
        if (!string.Equals(value.Path, TargetSnapshotPath, StringComparison.OrdinalIgnoreCase))
            TargetSnapshotPath = value.Path;
    }

    /// <summary>Drop the remembered row, never the file — the .dacpac is the operator's and may
    /// be a baseline another tool or machine still uses.</summary>
    private void ForgetSnapshot(bool isSource)
    {
        var entry = isSource ? SelectedSourceSnapshot : SelectedTargetSnapshot;
        if (entry is null) { StatusMessage = "Choose a snapshot in the list to forget it."; return; }
        if (!_snapshotLibrary.Forget(entry)) return;
        if (isSource) SelectedSourceSnapshot = null; else SelectedTargetSnapshot = null;
        StatusMessage = $"Forgotten {entry.FileName}. The file itself was left where it is.";
        AppendLog(StatusMessage);
    }

    partial void OnSelectedSavedComparisonChanged(SavedComparison? value) => ApplySavedComparison(value);

    /// <summary>
    /// Fill both cards from a saved pairing. A side whose profile or file has gone is named and
    /// left as it was rather than half-applied: a comparison that quietly compares something else
    /// is worse than one that refuses to.
    /// </summary>
    private void ApplySavedComparison(SavedComparison? comparison)
    {
        if (comparison is null) return;
        var missing = new List<string>();
        ApplyComparisonSide(comparison, isSource: true, missing);
        ApplyComparisonSide(comparison, isSource: false, missing);
        // The guards travel with the pairing on purpose: they decide what a script may destroy, so
        // re-running a comparison must not quietly widen them.
        AllowUnsafeDrops = comparison.AllowUnsafeDrops;
        AllowUnsafeChanges = comparison.AllowUnsafeChanges;
        ComparisonName = comparison.Name;
        StatusMessage = missing.Count > 0
            ? $"Comparison '{comparison.Name}' is incomplete: {string.Join("; ", missing)}."
            : $"Loaded comparison '{comparison.Name}': {comparison.SourceLabel} against {comparison.TargetLabel}.";
        AppendLog(StatusMessage);
    }

    private void ApplyComparisonSide(SavedComparison comparison, bool isSource, List<string> missing)
    {
        var side = isSource ? "Source" : "Target";
        var snapshotPath = isSource ? comparison.SourceSnapshotPath : comparison.TargetSnapshotPath;
        if (!string.IsNullOrWhiteSpace(snapshotPath))
        {
            if (isSource) { SourceIsSnapshot = true; SourceSnapshotPath = snapshotPath; }
            else { TargetIsSnapshot = true; TargetSnapshotPath = snapshotPath; }
            if (!File.Exists(snapshotPath))
                missing.Add($"the snapshot '{System.IO.Path.GetFileName(snapshotPath)}' is no longer there");
            return;
        }

        var profileId = isSource ? comparison.SourceProfileId : comparison.TargetProfileId;
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            var profile = SavedConnections.FirstOrDefault(p => p.Id == profileId);
            if (profile is null)
            {
                missing.Add($"the saved profile '{(isSource ? comparison.SourceLabel : comparison.TargetLabel)}' is no longer saved");
                return;
            }
            if (isSource) SourceIsSnapshot = false; else TargetIsSnapshot = false;
            _applyingProfile = true;
            try
            {
                if (isSource) SelectedSavedSource = profile; else SelectedSavedTarget = profile;
            }
            finally { _applyingProfile = false; }
            ApplyProfile(profile, isSource);
            return;
        }

        var server = isSource ? comparison.SourceServer : comparison.TargetServer;
        var database = isSource ? comparison.SourceDatabase : comparison.TargetDatabase;
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            missing.Add($"the {side.ToLowerInvariant()} names neither a profile, a database nor a snapshot");
            return;
        }
        if (isSource) { SourceIsSnapshot = false; SourceServer = server; SourceDatabase = database; }
        else { TargetIsSnapshot = false; TargetServer = server; TargetDatabase = database; }
    }

    /// <summary>Why this side cannot be remembered, or null when it can. Credentials are the
    /// reason: a pairing stores a profile's id, never its password, so a side that authenticates
    /// with a user name has to become a profile first.</summary>
    private string? ComparisonSideProblem(bool isSource)
    {
        var side = isSource ? "Source" : "Target";
        if (isSource ? SourceIsSnapshot : TargetIsSnapshot)
            return string.IsNullOrWhiteSpace(isSource ? SourceSnapshotPath : TargetSnapshotPath)
                ? $"{side} reads from a snapshot but no file is named."
                : null;
        var server = isSource ? SourceServer : TargetServer;
        var database = isSource ? SourceDatabase : TargetDatabase;
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            return $"{side} needs a database or a snapshot file before the pairing can be saved.";
        var profile = isSource ? SelectedSavedSource : SelectedSavedTarget;
        var auth = isSource ? SourceAuth : TargetAuth;
        if (profile is null && auth.NeedsCredentials)
            return $"{side} uses {auth.Display} and has no saved profile — save it as a profile first. A comparison never stores a password.";
        return null;
    }

    private void SaveComparison()
    {
        var name = ComparisonName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Type a name for this comparison before saving it.";
            return;
        }
        var problem = ComparisonSideProblem(isSource: true) ?? ComparisonSideProblem(isSource: false);
        if (problem != null)
        {
            StatusMessage = problem;
            AppendLog(problem);
            return;
        }

        var comparison = new SavedComparison
        {
            Name = name,
            SavedAt = DateTimeOffset.UtcNow,
            SourceProfileId = SourceIsSnapshot ? null : SelectedSavedSource?.Id,
            SourceSnapshotPath = SourceIsSnapshot ? SourceSnapshotPath : null,
            SourceServer = SourceIsSnapshot || SelectedSavedSource != null ? null : SourceServer,
            SourceDatabase = SourceIsSnapshot || SelectedSavedSource != null ? null : SourceDatabase,
            SourceLabel = SavedComparison.DescribeSide(SourceIsSnapshot, SourceSnapshotPath, SourceServer, SourceDatabase),
            TargetProfileId = TargetIsSnapshot ? null : SelectedSavedTarget?.Id,
            TargetSnapshotPath = TargetIsSnapshot ? TargetSnapshotPath : null,
            TargetServer = TargetIsSnapshot || SelectedSavedTarget != null ? null : TargetServer,
            TargetDatabase = TargetIsSnapshot || SelectedSavedTarget != null ? null : TargetDatabase,
            TargetLabel = SavedComparison.DescribeSide(TargetIsSnapshot, TargetSnapshotPath, TargetServer, TargetDatabase),
            AllowUnsafeDrops = AllowUnsafeDrops,
            AllowUnsafeChanges = AllowUnsafeChanges,
        };
        try
        {
            _savedComparisons.Save(comparison);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            AppendLog($"Saved comparison rejected: {ex.Message}");
            return;
        }
        // Selecting it re-applies the pairing, which writes its own status line — so the message
        // that says the save worked has to come after it.
        SelectedSavedComparison = _savedComparisons.Find(name);
        StatusMessage = $"Saved comparison '{name}' ({comparison.SourceLabel} against {comparison.TargetLabel}).";
        AppendLog(StatusMessage);
    }

    private void ForgetComparison()
    {
        var comparison = SelectedSavedComparison;
        if (comparison is null)
        {
            StatusMessage = "Choose a saved comparison to forget it.";
            return;
        }
        if (!_savedComparisons.Forget(comparison)) return;
        SelectedSavedComparison = null;
        StatusMessage = $"Forgotten comparison '{comparison.Name}'. The databases, profiles and files it pointed at are untouched.";
        AppendLog(StatusMessage);
    }

    partial void OnSourceIsSnapshotChanged(bool value)
    {
        OnPropertyChanged(nameof(SourceCardTitle));
        RefreshSnapshotCommands();
    }

    partial void OnTargetIsSnapshotChanged(bool value)
    {
        OnPropertyChanged(nameof(TargetCardTitle));
        // A file has nothing to test a connection against, so a stale "connected" banner from
        // when this side was live would be a lie about the side as it now stands.
        if (value) { TargetConnectionStatus = ""; TargetHasConnectionResult = false; }
        RefreshSnapshotCommands();
    }

    partial void OnIsCapturingSnapshotChanged(bool value) => RefreshSnapshotCommands();

    partial void OnShowAddedChanged(bool value) => ApplyFilter();
    partial void OnShowChangedChanged(bool value) => ApplyFilter();
    partial void OnShowDeletedChanged(bool value) => ApplyFilter();
    partial void OnSourceUseWindowsAuthChanged(bool value) { OnPropertyChanged(nameof(SourceUseWindowsAuth)); ShowError = false; ClearSourceSelectionOnManualEdit(); }
    partial void OnTargetUseWindowsAuthChanged(bool value) { OnPropertyChanged(nameof(TargetUseWindowsAuth)); ShowError = false; ClearTargetSelectionOnManualEdit(); }

    // The dropdown is the single UI source of truth for auth; UseWindowsAuth is kept in
    // sync because saved profiles, auditing and ConnectionInfo still speak in those terms.
    partial void OnSourceAuthChanged(AuthMethod value)
    {
        SourceUseWindowsAuth = value.IsWindows;
        if (!value.NeedsCredentials) SourcePassword = "";
        ShowError = false;
        ClearSourceSelectionOnManualEdit();
    }

    partial void OnTargetAuthChanged(AuthMethod value)
    {
        TargetUseWindowsAuth = value.IsWindows;
        if (!value.NeedsCredentials) TargetPassword = "";
        ShowError = false;
        ClearTargetSelectionOnManualEdit();
    }

    partial void OnSourceServerChanged(string value) { ClearSourceSelectionOnManualEdit(); RefreshSnapshotCommands(); }
    partial void OnSourceDatabaseChanged(string value) { ClearSourceSelectionOnManualEdit(); RefreshSnapshotCommands(); }
    partial void OnSourceUsernameChanged(string value) => ClearSourceSelectionOnManualEdit();
    partial void OnSourcePasswordChanged(string value) => ClearSourceSelectionOnManualEdit();
    partial void OnTargetServerChanged(string value) { ClearTargetSelectionOnManualEdit(); RefreshSnapshotCommands(); }
    partial void OnTargetDatabaseChanged(string value) { ClearTargetSelectionOnManualEdit(); RefreshSnapshotCommands(); }
    partial void OnTargetUsernameChanged(string value) => ClearTargetSelectionOnManualEdit();
    partial void OnTargetPasswordChanged(string value) => ClearTargetSelectionOnManualEdit();

    partial void OnHasDataMovePlanChanged(bool value) => ((RelayCommand)StartDataMoveCommand).NotifyCanExecuteChanged();
    partial void OnDataMoveFilterTextChanged(string value) => ApplyDataMoveFilter();
    partial void OnIsSidebarOpenChanged(bool value) => SidebarWidth = new GridLength(value ? 220 : 0);
    partial void OnUiScaleChanged(double value) => OnDisplaySettingChanged();
    partial void OnCodeFontSizeChanged(double value) => OnDisplaySettingChanged();
    partial void OnHasSelectionChanged(bool value) => RefreshScriptLayoutProps();
    partial void OnHasSourceScriptChanged(bool value) => RefreshScriptLayoutProps();
    partial void OnHasTargetScriptChanged(bool value) => RefreshScriptLayoutProps();
    partial void OnSourceScriptContentChanged(string value)
    {
        OnPropertyChanged(nameof(SingleScriptContent));
        OnPropertyChanged(nameof(SingleScriptHeader));
    }
    partial void OnTargetScriptContentChanged(string value)
    {
        OnPropertyChanged(nameof(SingleScriptContent));
        OnPropertyChanged(nameof(SingleScriptHeader));
    }

    private void RefreshScriptLayoutProps()
    {
        OnPropertyChanged(nameof(HasBothScripts));
        OnPropertyChanged(nameof(HasSingleScript));
        OnPropertyChanged(nameof(HasOnlySource));
        OnPropertyChanged(nameof(HasOnlyTarget));
        OnPropertyChanged(nameof(SingleScriptContent));
        OnPropertyChanged(nameof(SingleScriptHeader));
    }

    private void LoadDisplaySettings()
    {
        _applyingSettings = true;
        try
        {
            var s = _settingsService.Load();
            UiScale = s.UiScale;
            CodeFontSize = s.CodeFontSize;
            AppSettingsService.ApplyToResources(s);
            OnPropertyChanged(nameof(DisplaySettingsSummary));
        }
        finally { _applyingSettings = false; }
    }

    private void OnDisplaySettingChanged()
    {
        OnPropertyChanged(nameof(DisplaySettingsSummary));
        if (_applyingSettings)
            return;
        var ui = Math.Clamp(UiScale, AppSettings.MinUiScale, AppSettings.MaxUiScale);
        var code = Math.Clamp(CodeFontSize, AppSettings.MinCodeFontSize, AppSettings.MaxCodeFontSize);
        if (ui != UiScale || code != CodeFontSize)
        {
            _applyingSettings = true;
            try
            {
                UiScale = ui;
                CodeFontSize = code;
            }
            finally { _applyingSettings = false; }
        }
        AppSettingsService.ApplyToResources(UiScale, CodeFontSize);
        try { _settingsService.Save(new AppSettings { UiScale = UiScale, CodeFontSize = CodeFontSize }); }
        catch (Exception ex)
        {
            // The sizes still apply for this session; only saving them for next time failed.
            AppLog.Error("MainViewModel", ex, "UI settings could not be saved to disk");
            StatusMessage = "⚠ Text sizes apply now but could not be saved to disk.";
        }
    }

    private void ResetTextSettings()
    {
        UiScale = AppSettings.DefaultUiScale;
        CodeFontSize = AppSettings.DefaultCodeFontSize;
    }
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
                // Auth first: the hook clears the password for flows that have none.
                SourceAuth = profile.Auth;
                SourceUsername = profile.Username;
                SourcePassword = profile.Password;
                SourceEncryptConnection = profile.EncryptConnection;
                SourceTrustServerCertificate = profile.TrustServerCertificate;
                SourceHasConnectionResult = false;
            }
            else
            {
                TargetServer = profile.Server;
                TargetDatabase = profile.Database;
                TargetAuth = profile.Auth;
                TargetUsername = profile.Username;
                TargetPassword = profile.Password;
                TargetEncryptConnection = profile.EncryptConnection;
                TargetTrustServerCertificate = profile.TrustServerCertificate;
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
        var auth = isSource ? SourceAuth : TargetAuth;
        var username = isSource ? SourceUsername : TargetUsername;
        var password = isSource ? SourcePassword : TargetPassword;
        var encrypt = isSource ? SourceEncryptConnection : TargetEncryptConnection;
        var trustCert = isSource ? SourceTrustServerCertificate : TargetTrustServerCertificate;
        var side = isSource ? "Source" : "Target";

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            ErrorMessage = $"Cannot save {side} profile: server and database are required.";
            ShowError = true;
            return;
        }

        var existing = SavedConnections.FirstOrDefault(c =>
            c.Matches(server, database, useWinAuth, username, auth.AuthenticationMethod));
        if (existing != null)
        {
            _applyingProfile = true;
            try
            {
                existing.Server = server.Trim();
                existing.Database = database.Trim();
                existing.UseWindowsAuth = useWinAuth;
                existing.Authentication = auth.AuthenticationMethod;
                existing.Username = username?.Trim() ?? string.Empty;
                existing.Password = password ?? string.Empty;
                existing.EncryptConnection = encrypt;
                existing.TrustServerCertificate = trustCert;
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
                Authentication = auth.AuthenticationMethod,
                Username = username?.Trim() ?? string.Empty,
                Password = password ?? string.Empty,
                EncryptConnection = encrypt,
                TrustServerCertificate = trustCert,
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
        try
        {
            _savedService.Save(SavedConnections);
            if (_savedService.LastWarning is { } warning)
            {
                StatusMessage = "⚠ " + warning;
                AppendLog("WARNING: " + warning);
            }
        }
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
        var source = GetSource();
        var target = GetTarget();
        var problem = SideProblem(source, "Source") ?? SideProblem(target, "Target");
        if (problem is not null)
        { ShowError = true; ErrorMessage = problem; StatusMessage = problem; return; }

        IsComparing = true; ProgressValue = 0; Differences.Clear(); FilteredDifferences.Clear();
        HasResults = false; ShowError = false; ErrorMessage = "";
        FullDeployScript = ""; HasFullScript = false; SelectedDiff = null;
        RollbackScript = ""; ShowingRollbackScript = false;
        HasSourceScript = false; HasTargetScript = false;
        StatusMessage = "Comparing...";
        AppendLog($"--- Compare started: {source.DisplayName} -> {target.DisplayName} ---");
        try
        {
            var p = new Progress<string>(m =>
            {
                ProgressText = m;
                StatusMessage = m;
                AppendLog(m);
            });
            var (items, summary) = await _compareService.CompareAsync(source, target, p, allowUnsafeDrops: AllowUnsafeDrops, allowUnsafeChanges: AllowUnsafeChanges);
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
            FullDeployScript = await _compareService.GenerateScriptAsync(GetSource(), GetTarget(), allowUnsafeDrops: AllowUnsafeDrops, allowUnsafeChanges: AllowUnsafeChanges);
            HasFullScript = true; StatusMessage = "Deployment script generated.";
            AppendLog("Deployment script generated successfully.");
        }
        catch (Exception ex) { StatusMessage = $"Error generating script: {ex.Message}"; AppendLog($"Script generation ERROR: {ex.Message}"); }
        finally { IsComparing = false; }
    }

    private async Task GenerateRollbackScriptAsync()
    {
        if (!HasResults) return;
        IsComparing = true; StatusMessage = "Generating rollback script...";
        AppendLog("Generating rollback (DOWN) script...");
        try
        {
            RollbackScript = await _compareService.GenerateRollbackScriptAsync(GetSource(), GetTarget(), allowUnsafeChanges: AllowUnsafeChanges);
            ShowingRollbackScript = true;
            StatusMessage = "Rollback script generated.";
            // The reversed diff only exists while the target is behind, so the text is worth
            // keeping even though the app never runs it.
            AppendLog($"Rollback script generated ({RollbackScript.Length:N0} chars). Save it now — once the deploy runs, the reversed diff is empty.");
        }
        catch (Exception ex) { StatusMessage = $"Error generating rollback script: {ex.Message}"; AppendLog($"Rollback generation ERROR: {ex.Message}"); }
        finally { IsComparing = false; }
    }

    /// <summary>Name for the script the pane is currently showing, direction included.</summary>
    public string ScriptSuggestedFileName =>
        $"{(ShowingRollbackScript ? "rollback" : "deploy")}-{TargetDatabaseForFileName()}-{DateTime.UtcNow:yyyyMMdd-HHmm}.sql";

    private string TargetDatabaseForFileName()
    {
        // A snapshot target has no live database, so the name it was captured from is used —
        // the file name is the operator's cue for where the script belongs.
        var name = GetTarget().DatabaseName ?? GetSource().DatabaseName ?? "target";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "target" : name;
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

        // Reachable through the confirmation overlay even though the button is disabled for it.
        if (TargetIsSnapshot)
        {
            StatusMessage = ErrorMessage = "A snapshot file cannot be deployed to. Put a live database on the Target side.";
            ShowError = true;
            return;
        }

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
        // Deploy audit trail: what was pushed where, and which safety rails were off.
        AppLog.Info($"Apply started → target {TargetAuditLabel()}, source {SourceAuditLabel()}, " +
                    $"unsafeDrops={AllowUnsafeDrops}, unsafeChanges={AllowUnsafeChanges}, items={includedItems.Count}");
        try
        {
            var p = new Progress<string>(m => { ProgressText = m; StatusMessage = m; AppendLog(m); });
            var (_, script) = await _compareService.ApplyChangesAsync(GetSource(), GetTarget(), p, includedItems, allowUnsafeDrops: AllowUnsafeDrops, allowUnsafeChanges: AllowUnsafeChanges);
            FullDeployScript = script; HasFullScript = true;
            StatusMessage = "Changes applied successfully.";
            AppendLog("--- Apply finished successfully ---");
            AppLog.Info($"Apply succeeded → target {TargetAuditLabel()}, script {script.Length:N0} chars");
        }
        catch (Exception ex)
        {
            AppLog.Error("Apply", ex, $"Apply aborted → target {TargetAuditLabel()}");
            StatusMessage = $"Error applying changes: {ex.Message}";
            AppendLog($"Apply ERROR: {ex.Message}");
        }
        finally { IsComparing = false; }
    }

    /// <summary>Server/database only — audit lines must never carry credentials.</summary>
    // An audit line names the server, the database and the auth mode only — never a user
    // name or a password, because these labels are written to the log file. A side reading
    // from a file is named by that file, since no server was contacted for it.
    private string SourceAuditLabel() => SourceIsSnapshot
        ? $"snapshot [{Path.GetFileName(SourceSnapshotPath)}]"
        : $"{SourceServer?.Trim()}/{SourceDatabase?.Trim()} ({SourceAuth.Short})";

    private string TargetAuditLabel() => TargetIsSnapshot
        ? $"snapshot [{Path.GetFileName(TargetSnapshotPath)}]"
        : $"{TargetServer?.Trim()}/{TargetDatabase?.Trim()} ({TargetAuth.Short})";

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
        Authentication = SourceAuth.AuthenticationMethod,
        Username = SourceUsername, Password = SourcePassword,
        EncryptConnection = SourceEncryptConnection, TrustServerCertificate = SourceTrustServerCertificate
    };

    /// <summary>The two connections as other windows see them, so a window opened from
    /// here starts on the databases the user is already working with.</summary>
    public ConnectionInfo CurrentSourceConnection() => GetSourceInfo();
    public ConnectionInfo CurrentTargetConnection() => GetTargetInfo();

    private ConnectionInfo GetTargetInfo() => new()
    {
        Server = TargetServer, Database = TargetDatabase, UseWindowsAuth = TargetUseWindowsAuth,
        Authentication = TargetAuth.AuthenticationMethod,
        Username = TargetUsername, Password = TargetPassword,
        EncryptConnection = TargetEncryptConnection, TrustServerCertificate = TargetTrustServerCertificate
    };

    /// <summary>
    /// The two sides of the comparison. Each is either the database described above or the
    /// snapshot file chosen for it — never a mix, and never the other window's connection:
    /// data move and backup still talk to live databases through <see cref="ConnectionInfo"/>.
    /// </summary>
    private SchemaSource GetSource() => SourceIsSnapshot
        ? SchemaSource.OfSnapshot(SourceSnapshotPath, TryReadSnapshot(SourceSnapshotPath))
        : SchemaSource.OfDatabase(GetSourceInfo());

    private SchemaSource GetTarget() => TargetIsSnapshot
        ? SchemaSource.OfSnapshot(TargetSnapshotPath, TryReadSnapshot(TargetSnapshotPath))
        : SchemaSource.OfDatabase(GetTargetInfo());

    /// <summary>A missing or unreadable header costs the label its detail, not the comparison:
    /// DacFx reads the model from the file itself, so the diff is still correct.</summary>
    private SnapshotInfo? TryReadSnapshot(string path)
    {
        try { return SchemaSnapshotService.ReadSnapshot(path); }
        catch (Exception ex) { AppendLog($"Snapshot header unreadable: {ex.Message}"); return null; }
    }

    /// <summary>What stops a compare from starting, per side, or null when that side is ready.
    /// A snapshot has to exist on disk: naming a file that isn't there is a typo, and DacFx
    /// reports it as a comparison failure that reads like a bug in the diff.</summary>
    private static string? SideProblem(SchemaSource side, string label)
    {
        if (side.IsSnapshot)
            return File.Exists(side.SnapshotPath)
                ? null
                : $"{label} snapshot file was not found: {Path.GetFileName(side.SnapshotPath)}";
        var live = side.Connection!;
        if (string.IsNullOrWhiteSpace(live.Server)) return $"{label}: enter a server, or switch that side to a snapshot file.";
        if (string.IsNullOrWhiteSpace(live.Database)) return $"{label}: enter a database name, or switch that side to a snapshot file.";
        return null;
    }

    /// <summary>
    /// Writes the chosen side's current schema to a file. This only ever reads the database —
    /// extraction runs inside a transaction-free DAC export and touches no user rows.
    /// </summary>
    private async Task CaptureSnapshotAsync(bool isSource)
    {
        var live = isSource ? GetSourceInfo() : GetTargetInfo();
        var problem = SideProblem(SchemaSource.OfDatabase(live), isSource ? "Source" : "Target");
        if (problem is not null) { StatusMessage = ErrorMessage = problem; ShowError = true; return; }

        if (PickSnapshotSavePathAsync is null)
        {
            StatusMessage = "No file picker is available here, so the snapshot cannot be saved.";
            ErrorMessage = StatusMessage; ShowError = true;
            AppendLog("Snapshot capture aborted: the view supplied no save-path hook.");
            return;
        }

        var suggested = SchemaSnapshotService.SuggestedFileName(live.Database, DateTimeOffset.UtcNow);
        var target = await PickSnapshotSavePathAsync(suggested);
        if (target is null) { StatusMessage = "Snapshot capture cancelled."; return; }

        IsCapturingSnapshot = true;
        ShowError = false;
        AppendLog($"--- Snapshot capture started: {live.SafeForLog} -> {Path.GetFileName(target)} ---");
        try
        {
            var progress = new Progress<string>(m => { StatusMessage = m; AppendLog($"Snapshot: {m}"); });
            var info = await _snapshotService.CaptureAsync(live, target, progress);
            // The path is stored but the side stays live: capturing the target as a baseline is
            // something you do immediately before deploying to that same target, so flipping the
            // card to file mode would break the very workflow the capture was for. The caption
            // comes from the finished file, not from what was intended.
            if (isSource) SourceSnapshotPath = info.Path; else TargetSnapshotPath = info.Path;
            StatusMessage = $"Snapshot of {live.Database} saved to {Path.GetFileName(info.Path)}.";
            AppendLog($"--- Snapshot capture completed: {info.Caption} ---");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message; ShowError = true;
            StatusMessage = $"Snapshot capture failed: {ex.Message}";
            AppendLog($"Snapshot capture ERROR: {ex.Message}");
        }
        finally { IsCapturingSnapshot = false; }
    }

    /// <summary>Points a side at an existing snapshot file to compare against.</summary>
    private async Task BrowseSnapshotAsync(bool isSource)
    {
        if (PickSnapshotFileAsync is null)
        {
            StatusMessage = "No file picker is available here, so a snapshot cannot be chosen.";
            ErrorMessage = StatusMessage; ShowError = true;
            return;
        }
        var path = await PickSnapshotFileAsync();
        if (path is null) return;
        if (isSource) { SourceIsSnapshot = true; SourceSnapshotPath = path; }
        else          { TargetIsSnapshot = true; TargetSnapshotPath = path; }
        StatusMessage = $"Comparing against snapshot {Path.GetFileName(path)}.";
    }
}
