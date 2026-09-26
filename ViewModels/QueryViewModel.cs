using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.ViewModels;

/// <summary>
/// View-model for the SQL Query window: connection picker, open query tabs,
/// execution (F5 / Ctrl+Enter), cancellation, per-tab result grids, messages,
/// row caps, timing, and tab management. Mirrors SSMS query behavior.
/// </summary>
public partial class QueryViewModel : ObservableObject
{
    private readonly QueryExecutionService _service = new();
    private readonly SavedConnectionsService _savedService = new();
    private readonly QuerySchemaService _schemaService = new();
    private readonly RecentFilesService _recentService = new();
    private readonly TabSessionService _sessionService = new();
    private bool _restoringSession;
    private readonly QueryHistoryService _historyService = new();
    private int _tabCounter;

    public ObservableCollection<SavedConnection> SavedConnections { get; } = [];
    [ObservableProperty] private SavedConnection? _selectedConnection;
    partial void OnSelectedConnectionChanged(SavedConnection? value)
    {
        if (value != null && IsConnected)
            _ = ConnectAsync();
    }
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectedDatabaseLabel = "Not connected";

    public ObservableCollection<QueryTab> Tabs { get; } = [];
    [ObservableProperty] private QueryTab? _activeTab;
    [ObservableProperty] private int _selectedTabIndex = -1;

    [ObservableProperty] private string _statusMessage = "Select a connection and click Connect.";
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>When on, executions also capture the actual plan (SET STATISTICS XML)
    /// and render it as a diagram in the results pane.</summary>
    [ObservableProperty] private bool _showExecutionPlan;

    partial void OnShowExecutionPlanChanged(bool value) => OnPropertyChanged(nameof(PlanToggleText));

    /// <summary>When on, executions emit SET STATISTICS IO/TIME output (logical reads
    /// per table, execution times) plus SqlClient client statistics into Messages.</summary>
    [ObservableProperty] private bool _showIoTimeStats;

    partial void OnShowIoTimeStatsChanged(bool value) => OnPropertyChanged(nameof(IoTimeToggleText));

    /// <summary>Keywords offered by the ✨ Explain dropdown (aggregation-centric).</summary>
    public ObservableCollection<string> ExplainKeywords { get; } = new(
        new[] { "SUM", "AVG", "COUNT", "COUNT(*)", "COUNT_BIG", "MIN", "MAX", "STDEV", "STDEVP",
                "VAR", "VARP", "GROUP BY", "HAVING", "WHERE", "DISTINCT", "TOP", "ORDER BY",
                "INNER JOIN", "LEFT JOIN", "OFFSET / FETCH" });
    [ObservableProperty] private string _selectedKeyword = "HAVING";

    /// <summary>Set by the view — opens the ✨ keyword explainer dialog for a keyword.</summary>
    public Action<string>? ShowKeywordExplainer { get; set; }

    /// <summary>Set by the view — opens the 🧠 explain dialog for the active tab's SQL.</summary>
    public Action<string>? ShowQueryExplanation { get; set; }

    /// <summary>Set by the view to enable clipboard copy of results.
    /// Returns false when the clipboard was unavailable (status must not claim success).</summary>
    public Func<string, Task<bool>>? CopyToClipboardAsync { get; set; }

    /// <summary>Set by the view — asks the user for a save path (StorageProvider).
    /// Returns null when cancelled or unavailable.</summary>
    public Func<string, Task<string?>>? PickSavePathAsync { get; set; }

    /// <summary>Set by the view — asks for a .sql file to open. Null when cancelled.</summary>
    public Func<Task<string?>>? PickOpenSqlPathAsync { get; set; }

    /// <summary>Set by the view — asks for a .sql save path from a suggested name. Null when cancelled.</summary>
    public Func<string, Task<string?>>? PickSaveSqlPathAsync { get; set; }

    public ICommand OpenFileCommand { get; }
    public ICommand SaveFileCommand { get; }
    public ICommand SaveFileAsCommand { get; }
    public ICommand OpenHistoryCommand { get; }
    public ICommand InsertHistoryCommand { get; }
    public ICommand CopyHistoryCommand { get; }
    public ICommand ClearHistoryCommand { get; }

    /// <summary>Set by the view — pushes history text into the focused SQL editor.</summary>
    public Action<string>? InsertSqlAtCaret { get; set; }

    /// <summary>All recorded runs, newest first (unfiltered).</summary>
    public ObservableCollection<QueryHistoryEntry> History { get; } = [];

    /// <summary>History rows matching <see cref="HistoryFilter"/>.</summary>
    public ObservableCollection<QueryHistoryEntry> FilteredHistory { get; } = [];

    [ObservableProperty] private string _historyFilter = string.Empty;
    [ObservableProperty] private QueryHistoryEntry? _selectedHistoryEntry;

    partial void OnHistoryFilterChanged(string value) => ApplyHistoryFilter();

    /// <summary>Recently opened/saved .sql paths, newest first.</summary>
    public ObservableCollection<string> RecentSqlFiles { get; } = [];

    [ObservableProperty] private string? _selectedRecentFile;

    partial void OnSelectedRecentFileChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        OpenSqlFile(value);
        SelectedRecentFile = null;   // allow picking the same entry again
    }

    public ICommand ConnectCommand { get; }
    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand CloseOtherTabsCommand { get; }
    public ICommand CloseAllTabsCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ExecuteSelectionCommand { get; }
    public ICommand CopyResultsCommand { get; }
    public ICommand CopyWithHeadersCommand { get; }
    public ICommand SaveCsvCommand { get; }
    public ICommand SaveJsonCommand { get; }
    public ICommand SaveMarkdownCommand { get; }
    public ICommand SaveInsertScriptCommand { get; }
    public ICommand FormatSqlCommand { get; }
    public ICommand ClearResultsCommand { get; }
    public ICommand ExplainCommand { get; }
    public ICommand ExplainKeywordCommand { get; }
    public ICommand TogglePlanCommand { get; }
    public ICommand ToggleIoTimeCommand { get; }
    public ICommand EstimatedPlanCommand { get; }
    public ICommand ShowResultsViewCommand { get; }
    public ICommand ShowPlanViewCommand { get; }

    public string PlanToggleText => ShowExecutionPlan ? "🧭 Plan ON" : "🧭 Plan";

    public string IoTimeToggleText => ShowIoTimeStats ? "📊 IO/Time ON" : "📊 IO/Time";

    /// <summary>
    /// Set by the view after construction. Opens the Query Constructor dialog
    /// modal-over-this-window. Keep as a callback (not a command) so the dialog
    /// can be wired without re-tearing down the view-model on every close.
    /// </summary>
    public Action? OpenQueryBuilderAction { get; set; }

    /// <summary>Set by the view — opens the searchable query-history window.</summary>
    public Action? OpenHistoryWindowAction { get; set; }

    /// <summary>
    /// Replaces the SQL text of the currently active tab. Called by the Query
    /// Constructor dialog when the user chooses "Apply to Current Tab".
    /// </summary>
    public void ReplaceActiveTabSql(string sql)
    {
        if (ActiveTab == null) { NewTab(sql); return; }
        if (ActiveTab.IsExecuting) return;
        ActiveTab.SqlText = sql;
        StatusMessage = $"Replaced SQL in '{ActiveTab.Title}'.";
    }

    public QueryViewModel()
    {
        ConnectCommand    = new AsyncRelayCommand(ConnectAsync);
        NewTabCommand     = new RelayCommand(() => NewTab());
        CloseTabCommand   = new RelayCommand<QueryTab?>(t => CloseTab(t ?? ActiveTab));
        CloseOtherTabsCommand = new RelayCommand<QueryTab?>(t => CloseOtherTabs(t ?? ActiveTab));
        CloseAllTabsCommand   = new RelayCommand(CloseAllTabs);
        ExecuteCommand    = new AsyncRelayCommand<QueryTab?>(t => ExecuteAsync(t ?? ActiveTab, null));
        CancelCommand     = new RelayCommand<QueryTab?>(t => Cancel(t ?? ActiveTab));
        ExecuteSelectionCommand = new AsyncRelayCommand<string?>(sel => ExecuteAsync(ActiveTab, sel));
        CopyResultsCommand  = new AsyncRelayCommand<QueryTab?>(CopyResultsAsync);
        CopyWithHeadersCommand = new AsyncRelayCommand<QueryResultTable?>(CopyWithHeadersAsync);
        SaveCsvCommand = new AsyncRelayCommand<QueryResultTable?>(t => SaveResultAsync(t, "csv"));
        SaveJsonCommand = new AsyncRelayCommand<QueryResultTable?>(t => SaveResultAsync(t, "json"));
        SaveMarkdownCommand = new AsyncRelayCommand<QueryResultTable?>(t => SaveResultAsync(t, "md"));
        SaveInsertScriptCommand = new AsyncRelayCommand<QueryResultTable?>(t => SaveResultAsync(t, "sql"));
        FormatSqlCommand = new RelayCommand(FormatActiveSql);
        ClearResultsCommand = new RelayCommand<QueryTab?>(ClearResults);
        ExplainCommand      = new RelayCommand(ExplainActiveTab);
        ExplainKeywordCommand = new RelayCommand(() =>
        {
            if (string.IsNullOrWhiteSpace(SelectedKeyword)) return;
            if (ShowKeywordExplainer is { } show) show(SelectedKeyword);
        });
        TogglePlanCommand   = new RelayCommand(() => ShowExecutionPlan = !ShowExecutionPlan);
        ToggleIoTimeCommand = new RelayCommand(() => ShowIoTimeStats = !ShowIoTimeStats);
        EstimatedPlanCommand = new AsyncRelayCommand<QueryTab?>(t => EstimatedPlanAsync(t ?? ActiveTab));
        ShowResultsViewCommand = new RelayCommand(() => { if (ActiveTab != null) ActiveTab.ShowPlanView = false; });
        ShowPlanViewCommand = new RelayCommand(() => { if (ActiveTab != null) ActiveTab.ShowPlanView = true; });
        OpenFileCommand    = new AsyncRelayCommand(OpenSqlFileAsync);
        SaveFileCommand    = new AsyncRelayCommand(() => SaveSqlFileAsync(saveAs: false));
        SaveFileAsCommand  = new AsyncRelayCommand(() => SaveSqlFileAsync(saveAs: true));
        OpenHistoryCommand = new RelayCommand(() => OpenHistoryWindowAction?.Invoke());
        InsertHistoryCommand = new RelayCommand(() =>
        {
            if (SelectedHistoryEntry == null) return;
            if (InsertSqlAtCaret == null)
            {
                StatusMessage = "The SQL editor is not open, so nothing could be inserted.";
                return;
            }
            InsertSqlAtCaret(SelectedHistoryEntry.Sql);
            StatusMessage = $"Inserted query from {SelectedHistoryEntry.TimeText} into the editor.";
        });
        CopyHistoryCommand = new AsyncRelayCommand(async () =>
        {
            if (SelectedHistoryEntry == null) return;
            if (CopyToClipboardAsync != null && await CopyToClipboardAsync(SelectedHistoryEntry.Sql))
                StatusMessage = "Copied the history query to the clipboard.";
            else StatusMessage = "Copy failed: the system clipboard is unavailable.";
        });
        ClearHistoryCommand = new RelayCommand(() =>
        {
            _historyService.Clear();
            History.Clear();
            FilteredHistory.Clear();
            StatusMessage = "Query history cleared.";
        });

        LoadSavedConnections();
        _recentService.Load();
        SyncRecentList();
        foreach (var entry in _historyService.Load()) History.Add(entry);
        ApplyHistoryFilter();
        RestoreSession();
    }

    // ═══════════ tab session: remember what was open across restarts ═══════════

    /// <summary>
    /// Reopens last run's tabs. File-backed tabs re-read from disk (and drop with a
    /// log line if the file is gone); scratch tabs restore their stored text so an
    /// accidental close is not silent data loss.
    /// </summary>
    public void RestoreSession()
    {
        _restoringSession = true;
        List<TabSessionEntry> entries;
        try
        {
            entries = _sessionService.Load();
        }
        finally
        {
            _restoringSession = false;
        }

        var restored = 0;
        QueryTab? active = null;
        _restoringSession = true;
        try
        {
            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.FilePath))
                {
                    if (!File.Exists(entry.FilePath))
                    {
                        AppLog.Warn($"[Query] Skipped tab '{entry.Title}': {entry.FilePath} no longer exists.");
                        continue;
                    }
                    OpenSqlFile(entry.FilePath);
                    var tab = Tabs.FirstOrDefault(
                        t => string.Equals(t.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (tab != null && entry.IsActive) active = tab;
                    restored++;
                    continue;
                }

                var scratch = NewTab(entry.SqlText ?? "");
                scratch.Title = string.IsNullOrWhiteSpace(entry.Title) ? scratch.Title : entry.Title;
                scratch.IsDirty = true;
                if (entry.IsActive) active = scratch;
                restored++;
            }

            if (Tabs.Count == 0)
            {
                NewTab();
                return;
            }

            if (active != null)
            {
                ActiveTab = active;
                SelectedTabIndex = Tabs.IndexOf(active);
            }
            StatusMessage = restored > 0
                ? $"Restored {restored} query tab(s) from the previous session."
                : "Press F5 to execute.";
        }
        finally
        {
            _restoringSession = false;
            PersistSession();
        }
    }

    /// <summary>Called when the window closes and after any tab structure change.</summary>
    public void PersistSession()
    {
        if (!_restoringSession) _sessionService.Save(Tabs, ActiveTab);
    }

    private void LoadSavedConnections()
    {
        foreach (var c in _savedService.Load())
            SavedConnections.Add(c);
        SelectedConnection = SavedConnections.FirstOrDefault();
    }

    private async Task ConnectAsync()
    {
        if (SelectedConnection == null)
        {
            StatusMessage = "Select a saved connection first.";
            return;
        }
        var info = SelectedConnection.ToConnectionInfo();
        ShowError = false;
        ErrorMessage = string.Empty;
        StatusMessage = $"Connecting to {SelectedConnection.DisplayName}…";
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(info.ConnectionString);
            await conn.OpenAsync();
            IsConnected = true;
            ConnectedDatabaseLabel = $"{info.Server} / {info.Database}";
            StatusMessage = $"Connected to {ConnectedDatabaseLabel}. Press F5 to execute.";
            _ = RefreshSchemaCacheAsync(info);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectedDatabaseLabel = "Not connected";
            ErrorMessage = ex.Message;
            ShowError = true;
            StatusMessage = "Connection failed — see details.";
        }
    }

    /// <summary>Loads table/column names in the background for IntelliSense.</summary>
    private async Task RefreshSchemaCacheAsync(ConnectionInfo info)
    {
        try
        {
            StatusMessage = $"Connected to {ConnectedDatabaseLabel}. Loading schema for IntelliSense…";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var tables = await _schemaService.GetTablesAsync(info, cts.Token);
            var columns = await _schemaService.GetColumnsByTableAsync(info, cts.Token);
            Controls.SqlCompletionProvider.Tables = tables;
            Controls.SqlCompletionProvider.ColumnsByTable = columns;
            Controls.SqlCompletionProvider.NotifySchemaChanged();
            StatusMessage = $"Connected to {ConnectedDatabaseLabel}. Schema loaded ({tables.Count:N0} tables/views). Press F5 to execute.";
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[Query] Schema cache failed: {ex.Message}");
            StatusMessage = $"Connected to {ConnectedDatabaseLabel}. (Schema IntelliSense unavailable.) Press F5 to execute.";
        }
    }

    public QueryTab NewTab(string? sql = null)
    {
        _tabCounter++;
        var tab = new QueryTab
        {
            Title = $"Query {_tabCounter}",
            SqlText = sql ?? "SELECT TOP 100 *\r\nFROM dbo.TableName;\r\n"
        };
        tab.IsDirty = false;   // the initializer above counts as an edit
        Tabs.Add(tab);
        ActiveTab = tab;
        SelectedTabIndex = Tabs.Count - 1;
        StatusMessage = $"New tab: {tab.Title}.";
        PersistSession();
        return tab;
    }

    private void CloseTab(QueryTab? tab)
    {
        if (tab == null) return;
        Cancel(tab);
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (Tabs.Count == 0)
        {
            NewTab();
            return;
        }
        var next = Math.Clamp(index, 0, Tabs.Count - 1);
        ActiveTab = Tabs[next];
        SelectedTabIndex = next;
        PersistSession();
    }

    private void CloseOtherTabs(QueryTab? keep)
    {
        if (keep == null) return;
        foreach (var t in Tabs.Where(t => t != keep).ToList())
            Cancel(t);
        Tabs.Clear();
        Tabs.Add(keep);
        ActiveTab = keep;
        SelectedTabIndex = 0;
        PersistSession();
    }

    private void CloseAllTabs()
    {
        foreach (var t in Tabs.ToList())
            Cancel(t);
        Tabs.Clear();
        NewTab();
        PersistSession();
    }

    private void Cancel(QueryTab? tab)
    {
        try { tab?.ExecutionCts?.Cancel(); } catch { }
    }

    /// <summary>
    /// Executes a tab. When <paramref name="selection"/> is non-empty only the
    /// selected text runs (SSMS behavior); otherwise the whole tab script runs.
    /// </summary>
    private async Task ExecuteAsync(QueryTab? tab, string? selection)
    {
        if (tab == null) return;
        if (tab.IsExecuting)
        {
            tab.StatusMessage = "Already executing — press Stop to cancel.";
            return;
        }
        if (SelectedConnection == null || !IsConnected)
        {
            tab.StatusMessage = "Connect to a database first.";
            StatusMessage = "Connect to a database before executing.";
            return;
        }

        var hasSelection = !string.IsNullOrWhiteSpace(selection);
        var sql = hasSelection ? selection! : tab.SqlText;
        if (string.IsNullOrWhiteSpace(sql))
        {
            tab.StatusMessage = hasSelection
                ? "No text selected — select SQL first or press F5 for the whole tab."
                : "Nothing to execute — the tab is empty.";
            return;
        }

        var info = SelectedConnection.ToConnectionInfo();
        var usePlan = ShowExecutionPlan;
        var useStats = ShowIoTimeStats;
        var cts = new CancellationTokenSource();
        tab.ExecutionCts = cts;
        tab.IsExecuting = true;
        tab.Results.Clear();
        tab.Messages.Clear();
        tab.HasResults = false;
        tab.HasMessages = false;
        tab.Plan = null;
        tab.ShowPlanView = false;
        tab.StatusMessage = "Executing…";
        tab.ElapsedText = string.Empty;
        ShowError = false;
        ErrorMessage = string.Empty;
        StatusMessage = $"Executing {tab.Title}…";
        var started = DateTime.UtcNow;

        try
        {
            // Session-scoped: persists on this connection for every batch of the script.
            var statsPrefix = useStats ? "SET STATISTICS IO ON;\r\nSET STATISTICS TIME ON;\r\n\r\n" : "";
            var sqlToRun = (usePlan ? "SET STATISTICS XML ON;\r\n\r\n" : "") + statsPrefix + sql;
            var tables = await _service.ExecuteAsync(
                info, sqlToRun, ct: cts.Token,
                onStatus: msg =>
                {
                    tab.StatusMessage = msg;
                    StatusMessage = $"{tab.Title}: {msg}";
                },
                onMessage: useStats
                    ? msg =>
                    {
                        tab.Messages.Add(msg);
                        tab.HasMessages = true;
                    }
                    : null,
                captureClientStats: useStats);

            var planStatementCount = 0;
            if (usePlan)
            {
                var planTables = tables.Where(ExecutionPlanService.IsPlanTable).ToList();
                tab.Plan = ExecutionPlanService.Parse(planTables.SelectMany(ExecutionPlanService.ExtractPlanXmls));
                planStatementCount = tab.Plan.Statements.Count;
                tables = tables.Where(t => !ExecutionPlanService.IsPlanTable(t)).ToList();
                if (planStatementCount > 0)
                    tab.ShowPlanView = true;
            }

            foreach (var t in tables)
                tab.Results.Add(t);

            tab.HasResults = tab.Results.Count > 0;
            tab.SelectedResult = tab.Results.FirstOrDefault(r => r.Rows.Count > 0)
                                 ?? tab.Results.FirstOrDefault();
            tab.SelectedCellText = "No cell selected.";
            var elapsed = DateTime.UtcNow - started;
            tab.ElapsedText = $"Elapsed {elapsed.TotalSeconds:0.00}s";
            var scope = hasSelection ? "selection" : "script";
            tab.StatusMessage = tab.Results.Count == 0
                ? $"Completed {scope} in {elapsed.TotalSeconds:0.00}s — no results."
                : $"Completed {scope} in {elapsed.TotalSeconds:0.00}s — {tab.Results.Count} result(s).";
            var capped = tables.Count(t => t.IsTruncated);
            if (capped > 0)
                tab.StatusMessage += $" {capped} result set(s) capped at {QueryExecutionService.MaxRowsPerResult:N0} rows.";
            if (planStatementCount > 0)
                tab.StatusMessage += tab.Plan!.HasRuntimeStats
                    ? $" Execution plan captured ({planStatementCount} statement(s)) with actual rows, time and reads."
                    : $" Execution plan captured ({planStatementCount} statement(s)) — the server reported no runtime metrics, so every number is an estimate.";
            if (useStats)
                tab.StatusMessage += " IO/Time + client statistics in Messages.";
            StatusMessage = $"{tab.Title}: {tab.StatusMessage}";
            RecordHistory(tab, sql, elapsed, hasSelection, error: null);
        }
        catch (OperationCanceledException)
        {
            tab.StatusMessage = "Cancelled by user.";
            tab.Messages.Add("Query cancelled by user.");
            tab.HasMessages = true;
            StatusMessage = $"{tab.Title}: cancelled.";
        }
        catch (Exception ex)
        {
            tab.StatusMessage = $"Error: {ex.Message.Split('\n')[0]}";
            tab.Messages.Add($"Msg: {ex.Message}");
            tab.HasMessages = true;
            ErrorMessage = ex.Message;
            ShowError = true;
            StatusMessage = $"{tab.Title}: error — {ex.Message.Split('\n')[0]}";
            // Failed runs belong in history too — that is what the user wants to find again.
            RecordHistory(tab, sql, DateTime.UtcNow - started, hasSelection, ex.Message.Split('\n')[0]);
        }
        finally
        {
            tab.IsExecuting = false;
            tab.ExecutionCts?.Dispose();
            tab.ExecutionCts = null;
        }
    }

    private void ClearResults(QueryTab? tab)
    {
        if (tab == null) return;
        tab.Results.Clear();
        tab.Messages.Clear();
        tab.HasResults = false;
        tab.HasMessages = false;
        tab.SelectedResult = null;
        tab.SelectedCellText = "No cell selected.";
        tab.StatusMessage = "Results cleared.";
        tab.ElapsedText = string.Empty;
    }

    /// <summary>
    /// SSMS "Display Estimated Execution Plan" (Ctrl+L): compiles the script under
    /// SET SHOWPLAN_XML ON so the server returns the plan WITHOUT executing it.
    /// The SET statements must each be the only statement in their batch (GO).
    /// </summary>
    private async Task EstimatedPlanAsync(QueryTab? tab)
    {
        if (tab == null) return;
        if (tab.IsExecuting)
        {
            tab.StatusMessage = "Already executing — press Stop to cancel.";
            return;
        }
        if (SelectedConnection == null || !IsConnected)
        {
            tab.StatusMessage = "Connect to a database first.";
            return;
        }
        var sql = tab.SqlText;
        if (string.IsNullOrWhiteSpace(sql))
        {
            tab.StatusMessage = "Nothing to estimate — the tab is empty.";
            return;
        }

        var info = SelectedConnection.ToConnectionInfo();
        var cts = new CancellationTokenSource();
        tab.ExecutionCts = cts;
        tab.IsExecuting = true;
        tab.Results.Clear();
        tab.Messages.Clear();
        tab.HasResults = false;
        tab.HasMessages = false;
        tab.Plan = null;
        tab.ShowPlanView = false;
        tab.StatusMessage = "Compiling estimated plan…";
        tab.ElapsedText = string.Empty;
        ShowError = false;
        ErrorMessage = string.Empty;
        StatusMessage = $"Estimating plan for {tab.Title}…";
        var started = DateTime.UtcNow;

        try
        {
            var wrapped = "SET SHOWPLAN_XML ON\r\nGO\r\n" + sql + "\r\nGO\r\nSET SHOWPLAN_XML OFF";
            var tables = await _service.ExecuteAsync(
                info, wrapped, ct: cts.Token,
                onStatus: msg =>
                {
                    tab.StatusMessage = msg;
                    StatusMessage = $"{tab.Title}: {msg}";
                });

            var planXmls = tables.Where(ExecutionPlanService.IsPlanTable)
                .SelectMany(ExecutionPlanService.ExtractPlanXmls).ToList();
            tab.Plan = ExecutionPlanService.Parse(planXmls);
            var statementCount = tab.Plan.Statements.Count;

            tab.ElapsedText = $"Elapsed {(DateTime.UtcNow - started).TotalSeconds:0.00}s";
            if (statementCount > 0)
            {
                tab.ShowPlanView = true;
                tab.Messages.Add($"Estimated plan: {statementCount} statement(s) compiled — nothing was executed.");
                tab.HasMessages = true;
                tab.StatusMessage = $"Estimated plan ready ({statementCount} statement(s)) — query was NOT executed, so the diagram holds estimates only.";
            }
            else
            {
                tab.StatusMessage = "No estimated plan returned — check the script (some statements cannot be compiled, e.g. USE).";
            }
            StatusMessage = $"{tab.Title}: {tab.StatusMessage}";
        }
        catch (OperationCanceledException)
        {
            tab.StatusMessage = "Cancelled by user.";
            tab.Messages.Add("Plan estimation cancelled by user.");
            tab.HasMessages = true;
            StatusMessage = $"{tab.Title}: cancelled.";
        }
        catch (Exception ex)
        {
            tab.StatusMessage = $"Error: {ex.Message.Split('\n')[0]}";
            tab.Messages.Add($"Msg: {ex.Message}");
            tab.HasMessages = true;
            ErrorMessage = ex.Message;
            ShowError = true;
            StatusMessage = $"{tab.Title}: error — {ex.Message.Split('\n')[0]}";
        }
        finally
        {
            tab.IsExecuting = false;
            tab.ExecutionCts?.Dispose();
            tab.ExecutionCts = null;
        }
    }

    /// <summary>
    /// Deterministic plain-English explanation of the active tab's SQL (no AI) —
    /// written into the tab's Messages pane so it scrolls with execution output.
    /// </summary>
    private void ExplainActiveTab()
    {
        var tab = ActiveTab;
        if (tab == null) return;
        var sql = tab.SqlText;
        if (string.IsNullOrWhiteSpace(sql))
        {
            tab.StatusMessage = "Nothing to explain — the tab is empty.";
            return;
        }
        try
        {
            var explanation = SqlExplainerService.Explain(sql);
            tab.Messages.Add($"Plain English: {explanation}");
            tab.HasMessages = true;
            tab.StatusMessage = "Explained in plain English — see Messages below.";
            StatusMessage = $"{tab.Title}: {explanation}";
            ShowQueryExplanation?.Invoke(sql);
        }
        catch (Exception ex)
        {
            tab.Messages.Add($"Plain English: could not analyze this script ({ex.Message}).");
            tab.HasMessages = true;
            tab.StatusMessage = "Could not analyze this script.";
        }
    }

    private async Task CopyResultsAsync(QueryTab? tab)
    {
        if (tab == null || CopyToClipboardAsync == null) return;
        var source = tab.SelectedResult is { Rows.Count: > 0 } selected
            ? selected
            : tab.Results.FirstOrDefault(r => r.Rows.Count > 0);
        if (source == null)
        {
            tab.StatusMessage = "Nothing to copy — no result rows.";
            return;
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join("\t", source.Columns));
        foreach (var row in source.Rows.Take(1_000))
            sb.AppendLine(string.Join("\t", source.Columns.Select(c =>
                row.TryGetValue(c, out var v) ? v?.ToString() ?? "NULL" : "NULL")));
        if (await CopyToClipboardAsync(sb.ToString()))
            tab.StatusMessage = $"Copied {Math.Min(source.Rows.Count, 1_000):N0} row(s) from “{source.Title}” (TSV).";
        else
            tab.StatusMessage = "Copy failed: the system clipboard is unavailable (another app may be holding it). Nothing was changed.";
    }

    /// <summary>SSMS "Copy with Headers": the whole result set incl. the header row (TSV).</summary>
    private async Task CopyWithHeadersAsync(QueryResultTable? table)
    {
        var tab = ActiveTab;
        if (table == null || CopyToClipboardAsync == null) return;
        if (table.Rows.Count == 0)
        {
            if (tab != null) tab.StatusMessage = "Nothing to copy — this result set has no rows.";
            return;
        }
        if (await CopyToClipboardAsync(ResultsExportService.ToTsv(table)))
            tab?.StatusMessage = $"Copied {table.Rows.Count:N0} row(s) with headers from “{table.Title}” (TSV).";
        else
            tab?.StatusMessage = "Copy failed: the system clipboard is unavailable. Nothing was changed.";
    }

    /// <summary>SSMS "Save Results As…": the result set as CSV, JSON, Markdown or
    /// INSERT scripts, chosen by the menu item that fired.</summary>
    private async Task SaveResultAsync(QueryResultTable? table, string format)
    {
        var tab = ActiveTab;
        if (table == null || PickSavePathAsync == null) return;
        if (table.Rows.Count == 0)
        {
            if (tab != null) tab.StatusMessage = "Nothing to save — this result set has no rows.";
            return;
        }

        string body;
        if (format == "json") body = ResultsExportService.ToJson(table);
        else if (format == "md") body = ResultsExportService.ToMarkdown(table);
        else if (format == "sql")
        {
            // The INSERT target comes from the script's own FROM clause; a join or a
            // non-SELECT has none, and guessing a table to write into is not acceptable.
            var target = ResultsExportService.InsertTargetFrom(tab?.SqlText);
            if (target == null)
            {
                if (tab != null)
                    tab.StatusMessage = "Cannot build INSERT scripts: this result does not come " +
                                        "from one table, so there is no target to name.";
                return;
            }
            body = ResultsExportService.ToInsertScripts(table, target);
        }
        else body = ResultsExportService.ToCsv(table);

        var label = format switch { "json" => "JSON", "md" => "Markdown", "sql" => "INSERT script", _ => "CSV" };
        var path = await PickSavePathAsync($"results_{DateTime.Now:yyyyMMdd_HHmmss}.{format}");
        if (string.IsNullOrWhiteSpace(path))
        {
            if (tab != null) tab.StatusMessage = "Save cancelled — no file was written.";
            return;
        }
        try
        {
            await File.WriteAllTextAsync(path, body, new System.Text.UTF8Encoding(true));
            var rows = Math.Min(table.Rows.Count, ResultsExportService.DefaultMaxRows);
            if (tab != null) tab.StatusMessage = $"Saved {rows:N0} row(s) as {label} to {path}";
            StatusMessage = tab != null ? $"{tab.Title}: saved {label} to {path}" : StatusMessage;
        }
        catch (Exception ex)
        {
            if (tab != null) tab.StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    /// <summary>Beautify the active tab's script in place. Nothing is ever
    /// rewritten — only case, whitespace and line breaks — and the tab goes dirty
    /// so the change is undoable/saveable like any other edit.</summary>
    private void FormatActiveSql()
    {
        var tab = ActiveTab;
        if (tab == null)
        {
            StatusMessage = "Nothing to beautify — open a query tab first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(tab.SqlText))
        {
            tab.StatusMessage = "Nothing to beautify — this tab is empty.";
            return;
        }
        var formatted = SqlFormatter.Format(tab.SqlText);
        if (formatted.TrimEnd() == tab.SqlText.TrimEnd())
        {
            tab.StatusMessage = "Already beautified — nothing changed.";
            return;
        }
        tab.SqlText = formatted;
        tab.StatusMessage = "Script beautified: keywords upper-cased, one clause per line, blocks indented.";
        StatusMessage = $"{tab.Title}: script beautified.";
    }

    // ═══════════ .sql files: open / save / recents ═══════════
    private async Task OpenSqlFileAsync()
    {
        if (PickOpenSqlPathAsync == null)
        {
            StatusMessage = "Opening a file needs the system file picker.";
            return;
        }
        var path = await PickOpenSqlPathAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Open cancelled.";
            return;
        }
        OpenSqlFile(path);
    }

    /// <summary>
    /// Loads a .sql file into a tab — reuses the tab that already holds the file.
    /// Also the entry point for drag-and-drop from Explorer.
    /// </summary>
    public void OpenSqlFile(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            var tab = Tabs.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (tab == null)
                tab = NewTab();          // NewTab selects it and clears the dirty flag below
            else
            {
                ActiveTab = tab;
                SelectedTabIndex = Tabs.IndexOf(tab);
            }

            tab.IsReloadingFromFile = true;
            try { tab.SqlText = content; }
            finally { tab.IsReloadingFromFile = false; }
            tab.MarkSavedAt(path);
            tab.StatusMessage = $"Opened {path} ({content.Length:N0} characters).";
            StatusMessage = $"Opened {Path.GetFileName(path)}.";
            _recentService.Touch(path);
            SyncRecentList();
            AppLog.Info($"Opened SQL file {path}");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(QueryViewModel), ex, $"Could not open {path}");
            StatusMessage = $"Could not open {path}: {ex.Message}";
        }
    }

    private async Task SaveSqlFileAsync(bool saveAs)
    {
        var tab = ActiveTab;
        if (tab == null) return;
        if (PickSaveSqlPathAsync == null)
        {
            tab.StatusMessage = "Saving needs the system file picker.";
            return;
        }
        var target = saveAs || string.IsNullOrWhiteSpace(tab.FilePath)
            ? await PickSaveSqlPathAsync(SuggestedSqlName(tab))
            : tab.FilePath;
        if (string.IsNullOrWhiteSpace(target))
        {
            tab.StatusMessage = "Save cancelled — nothing was written.";
            return;
        }
        try
        {
            await File.WriteAllTextAsync(target, tab.SqlText, new System.Text.UTF8Encoding(true));
            tab.MarkSavedAt(target);
            _recentService.Touch(target);
            SyncRecentList();
            tab.StatusMessage = $"Saved {target}";
            StatusMessage = $"{tab.Title}: saved.";
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(QueryViewModel), ex, $"Could not save to {target}");
            tab.StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    private static string SuggestedSqlName(QueryTab tab)
    {
        var stem = Path.GetFileNameWithoutExtension(tab.FilePath ?? tab.Title);
        var safe = new string(stem.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? $"query_{DateTime.Now:yyyyMMdd_HHmmss}.sql" : safe + ".sql";
    }

    public void SyncRecentList()
    {
        RecentSqlFiles.Clear();
        foreach (var path in _recentService.Paths)
            RecentSqlFiles.Add(path);
    }

    // ═══════════ query history ═══════════

    /// <summary>Rebuilds the filtered view the history window binds to.</summary>
    public void ApplyHistoryFilter()
    {
        FilteredHistory.Clear();
        foreach (var entry in History.Where(e => e.Matches(HistoryFilter)))
            FilteredHistory.Add(entry);
        SelectedHistoryEntry = FilteredHistory.FirstOrDefault();
    }

    private void RecordHistory(QueryTab tab, string sql, TimeSpan elapsed, bool fromSelection, string? error)
    {
        var entry = new QueryHistoryEntry
        {
            Sql = sql,
            ExecutedAt = DateTime.Now,
            Server = SelectedConnection?.Server ?? string.Empty,
            Database = SelectedConnection?.Database ?? string.Empty,
            DurationSeconds = elapsed.TotalSeconds,
            Scope = fromSelection ? "selection" : "script",
            Error = error,
            ResultSets = error == null ? tab.Results.Count : 0,
            TotalRows = error == null ? tab.Results.Sum(r => r.Rows.Count) : 0
        };
        try
        {
            _historyService.Add(entry);
            History.Insert(0, entry);
            while (History.Count > QueryHistoryService.MaxEntries)
                History.RemoveAt(History.Count - 1);
            ApplyHistoryFilter();
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(QueryViewModel), ex, "Query history could not be recorded");
        }
    }
}
