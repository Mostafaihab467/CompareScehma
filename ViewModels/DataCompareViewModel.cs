using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.ViewModels;

/// <summary>
/// View-model of the data compare window: two connections, the tables both sides share,
/// the row-level verdict per table and the synchronization script that follows from it.
/// Nothing here writes to a database — the script is the only output.
/// </summary>
public partial class DataCompareViewModel : ObservableObject
{
    private readonly DataCompareService _service = new();
    private readonly SavedConnectionsService _savedService = new();
    private readonly Dictionary<string, TableDataDiff> _diffs = new(StringComparer.OrdinalIgnoreCase);
    private bool _applyingProfile;

    public ObservableCollection<SavedConnection> SavedConnections { get; } = [];
    public ObservableCollection<DataCompareTable> Tables { get; } = [];
    public ObservableCollection<RowDiff> SelectedRows { get; } = [];

    /// <summary>Authentication dropdown contents (Windows, SQL, Entra ID flows).</summary>
    public IReadOnlyList<AuthMethod> AuthMethods { get; } = AuthMethod.All;

    /// <summary>Choices for the per-table row cap; anything above is a partial scan.</summary>
    public IReadOnlyList<int> RowLimitOptions { get; } = [1_000, 5_000, 20_000, 100_000];

    /// <summary>Text scale, taken from the saved settings like every other window.</summary>
    [ObservableProperty] private double _uiScale = AppSettings.DefaultUiScale;

    [ObservableProperty] private string _sourceServer = "";
    [ObservableProperty] private string _sourceDatabase = "";
    [ObservableProperty] private AuthMethod _sourceAuth = AuthMethod.Windows;
    [ObservableProperty] private string _sourceUsername = "";
    [ObservableProperty] private string _sourcePassword = "";
    [ObservableProperty] private SavedConnection? _selectedSavedSource;

    [ObservableProperty] private string _targetServer = "";
    [ObservableProperty] private string _targetDatabase = "";
    [ObservableProperty] private string _targetUsername = "";
    [ObservableProperty] private string _targetPassword = "";
    [ObservableProperty] private AuthMethod _targetAuth = AuthMethod.Windows;
    [ObservableProperty] private SavedConnection? _selectedSavedTarget;

    [ObservableProperty] private DataCompareTable? _selectedTable;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _statusMessage = "Fill in both connections and list the shared tables.";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _scriptText = "";
    [ObservableProperty] private bool _includeDeletes;
    [ObservableProperty] private int _rowLimit = DataCompareService.DefaultRowLimit;
    [ObservableProperty] private bool _hasTables;
    [ObservableProperty] private bool _hasScript;

    public ICommand DiscoverCommand { get; }
    public ICommand CompareCommand { get; }
    public ICommand GenerateScriptCommand { get; }
    public ICommand CopyScriptCommand { get; }
    public ICommand SaveScriptCommand { get; }

    /// <summary>Set by the view: (suggested name, extension) → chosen path or null.</summary>
    public Func<string, string, Task<string?>>? PickSaveFileAsync { get; set; }

    /// <summary>Set by the view to move text to the clipboard.</summary>
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

    public DataCompareViewModel()
    {
        foreach (var saved in _savedService.Load()) SavedConnections.Add(saved);
        try { UiScale = new AppSettingsService().Load().UiScale; }
        catch { /* the default scale is fine */ }
        DiscoverCommand = new AsyncRelayCommand(DiscoverAsync, () => !IsBusy);
        CompareCommand = new AsyncRelayCommand(CompareAsync, () => HasTables && !IsBusy);
        GenerateScriptCommand = new AsyncRelayCommand(GenerateScriptAsync, () => !IsBusy);
        CopyScriptCommand = new AsyncRelayCommand(async () =>
        {
            if (CopyToClipboardAsync != null && ScriptText.Length > 0)
                await CopyToClipboardAsync(ScriptText);
        });
        SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync, () => HasScript);
    }

    /// <summary>Prefills both sides from the main window's Source and Target connections.</summary>
    public void InitializeFrom(ConnectionInfo source, ConnectionInfo target)
    {
        _applyingProfile = true;
        Apply(source, isSource: true);
        Apply(target, isSource: false);
        _applyingProfile = false;
        return;

        void Apply(ConnectionInfo info, bool isSource)
        {
            var auth = AuthMethod.For(info.Authentication, info.UseWindowsAuth);
            if (isSource)
            {
                SourceServer = info.Server; SourceDatabase = info.Database; SourceAuth = auth;
                SourceUsername = info.Username; SourcePassword = info.Password;
                SelectedSavedSource = Match(info);
            }
            else
            {
                TargetServer = info.Server; TargetDatabase = info.Database; TargetAuth = auth;
                TargetUsername = info.Username; TargetPassword = info.Password;
                SelectedSavedTarget = Match(info);
            }
        }

        SavedConnection? Match(ConnectionInfo info) => SavedConnections.FirstOrDefault(p =>
            p.Matches(info.Server, info.Database, info.UseWindowsAuth, info.Username, info.Authentication));
    }
    private ConnectionInfo BuildSource() => new()
    {
        Server = SourceServer, Database = SourceDatabase,
        UseWindowsAuth = SourceAuth.IsWindows, Authentication = SourceAuth.AuthenticationMethod,
        Username = SourceUsername, Password = SourcePassword
    };

    private ConnectionInfo BuildTarget() => new()
    {
        Server = TargetServer, Database = TargetDatabase,
        UseWindowsAuth = TargetAuth.IsWindows, Authentication = TargetAuth.AuthenticationMethod,
        Username = TargetUsername, Password = TargetPassword
    };

    partial void OnSelectedSavedSourceChanged(SavedConnection? value)
    {
        if (_applyingProfile || value == null) return;
        _applyingProfile = true;
        SourceServer = value.Server; SourceDatabase = value.Database;
        SourceAuth = value.Auth; SourceUsername = value.Username; SourcePassword = value.Password;
        _applyingProfile = false;
    }

    partial void OnSelectedSavedTargetChanged(SavedConnection? value)
    {
        if (_applyingProfile || value == null) return;
        _applyingProfile = true;
        TargetServer = value.Server; TargetDatabase = value.Database;
        TargetAuth = value.Auth; TargetUsername = value.Username; TargetPassword = value.Password;
        _applyingProfile = false;
    }

    partial void OnSelectedTableChanged(DataCompareTable? value)
    {
        OnPropertyChanged(nameof(HasSelectedTable));
        SelectedRows.Clear();
        if (value == null || !_diffs.TryGetValue(value.FullName, out var diff)) return;
        foreach (var row in diff.Rows) SelectedRows.Add(row);
    }

    public bool HasSelectedTable => SelectedTable != null;

    private static string Label(ConnectionInfo info) => $"{info.Server}/{info.Database}";

    private bool MissingConnection()
    {
        var s = BuildSource();
        var t = BuildTarget();
        if (string.IsNullOrWhiteSpace(s.Server) || string.IsNullOrWhiteSpace(t.Server))
        { Fail("Enter both source and target servers."); return true; }
        if (string.IsNullOrWhiteSpace(s.Database) || string.IsNullOrWhiteSpace(t.Database))
        { Fail("Enter both database names."); return true; }
        return false;

        void Fail(string message)
        {
            ErrorMessage = message; ShowError = true; StatusMessage = message;
        }
    }

    private async Task DiscoverAsync()
    {
        if (MissingConnection()) return;
        IsBusy = true; ShowError = false;
        var source = BuildSource();
        var target = BuildTarget();
        try
        {
            StatusMessage = $"Listing the tables {Label(source)} and {Label(target)} share…";
            var (tables, warning) = await _service.DiscoverAsync(source, target);
            Tables.Clear();
            _diffs.Clear();
            foreach (var table in tables) Tables.Add(table);
            HasTables = Tables.Any(t => t.IsComparable);
            HasScript = false; ScriptText = "";
            var comparable = Tables.Count(t => t.IsComparable);
            SummaryText = $"{comparable} of {Tables.Count} table(s) can be compared by key." +
                          (warning is null ? "" : $" {warning}");
            StatusMessage = warning ?? $"Found {comparable} comparable table(s) in {Label(target)}.";
            AppLog.Info($"Data compare listed {comparable}/{Tables.Count} table(s): " +
                        $"{source.SafeForLog} vs {target.SafeForLog}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message; ShowError = true; StatusMessage = "Could not list the tables.";
            AppLog.Error("DataCompare", ex, "discover failed");
        }
        finally { IsBusy = false; }
    }

    private async Task CompareAsync()
    {
        if (MissingConnection()) return;
        var selected = Tables.Where(t => t.IsSelected && t.IsComparable).ToList();
        if (selected.Count == 0)
        {
            ErrorMessage = "Tick at least one table with a shared primary key.";
            ShowError = true; StatusMessage = ErrorMessage;
            return;
        }

        IsBusy = true; ShowError = false; _diffs.Clear(); HasScript = false; ScriptText = "";
        var source = BuildSource();
        var target = BuildTarget();
        var different = 0;
        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var table = selected[i];
                StatusMessage = $"[{i + 1}/{selected.Count}] Comparing {table.FullName} row by row…";
                TableDataDiff diff;
                try
                {
                    diff = await _service.CompareAsync(source, target, table, RowLimit, IncludeDeletes);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    table.State = TableDataState.Skipped;
                    SummaryText = $"{table.FullName} could not be compared: {ex.Message}";
                    AppLog.Error("DataCompare", ex, $"compare {table.FullName} failed");
                    continue;
                }

                _diffs[table.FullName] = diff;
                table.SourceRows = diff.SourceRows;
                table.TargetRows = diff.TargetRows;
                table.MissingInTarget = diff.MissingInTarget;
                table.ExtraInTarget = diff.ExtraInTarget;
                table.ChangedRows = diff.ChangedRows;
                table.State = diff.State;
                if (diff.State != TableDataState.Identical) different++;
            }

            HasTables = true;
            SummaryText = $"{selected.Count} table(s) scanned at up to {RowLimit:N0} rows each — " +
                          $"{different} differ, {selected.Count - different} identical. " +
                          (IncludeDeletes ? "Target-only rows will be deleted by the script."
                                           : "Target-only rows are reported but not deleted.");
            StatusMessage = different == 0
                ? "No row-level differences found."
                : $"{different} table(s) differ — generate the synchronization script.";
            AppLog.Info($"Data compare finished: {different}/{selected.Count} table(s) differ " +
                        $"between {source.SafeForLog} and {target.SafeForLog}");
        }
        finally { IsBusy = false; }
    }
    /// <summary>Builds the synchronization script from the comparisons already made.
    /// Tables that were never compared are named in the header rather than silently
    /// dropped, so a partial run cannot read as a complete answer.</summary>
    private async Task GenerateScriptAsync()
    {
        if (_diffs.Count == 0)
        {
            ErrorMessage = "Compare at least one table before generating a script.";
            ShowError = true; StatusMessage = ErrorMessage;
            return;
        }

        var source = BuildSource();
        var target = BuildTarget();
        try
        {
            var diffs = _diffs.Values.ToList();
            var script = await Task.Run(() => DataCompareService.BuildSyncScript(source, target, diffs, IncludeDeletes));
            var skipped = Tables.Count(t => t.IsSelected && t.IsComparable && !_diffs.ContainsKey(t.FullName));
            if (skipped > 0)
                script += $"-- {skipped} selected table(s) were not compared and are not covered by this script.\n";
            ScriptText = script;
            HasScript = true;
            StatusMessage = $"Sync script ready — {diffs.Count} compared table(s), " +
                            $"{diffs.Count(d => d.State != TableDataState.Identical)} with differences.";
            AppLog.Info($"Data compare script written: {script.Length:N0} chars, " +
                        $"{source.SafeForLog} → {target.SafeForLog}, deletes={IncludeDeletes}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message; ShowError = true; StatusMessage = "Script generation failed.";
            AppLog.Error("DataCompare", ex, "script generation failed");
        }
    }

    private async Task SaveScriptAsync()
    {
        if (!HasScript || PickSaveFileAsync == null)
        {
            StatusMessage = "Saving is not available from this window.";
            return;
        }
        var suggested = $"{(string.IsNullOrWhiteSpace(TargetDatabase) ? "data_sync" : TargetDatabase)}_sync.sql";
        var path = await PickSaveFileAsync(suggested, ".sql");
        if (path == null) { StatusMessage = "Script not saved."; return; }
        if (!path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) path += ".sql";
        await File.WriteAllTextAsync(path, ScriptText);
        StatusMessage = $"Script saved to {path}";
    }
}
