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
    private int _tabCounter;

    public ObservableCollection<SavedConnection> SavedConnections { get; } = [];
    [ObservableProperty] private SavedConnection? _selectedConnection;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectedDatabaseLabel = "Not connected";

    public ObservableCollection<QueryTab> Tabs { get; } = [];
    [ObservableProperty] private QueryTab? _activeTab;
    [ObservableProperty] private int _selectedTabIndex = -1;

    [ObservableProperty] private string _statusMessage = "Select a connection and click Connect.";
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>Set by the view to enable clipboard copy of results.</summary>
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

    public ICommand ConnectCommand { get; }
    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand CloseOtherTabsCommand { get; }
    public ICommand CloseAllTabsCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ExecuteSelectionCommand { get; }
    public ICommand CopyResultsCommand { get; }
    public ICommand ClearResultsCommand { get; }

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
        ClearResultsCommand = new RelayCommand<QueryTab?>(ClearResults);

        LoadSavedConnections();
        NewTab();
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
            StatusMessage = $"Connected to {ConnectedDatabaseLabel}. Schema loaded ({tables.Count:N0} tables/views). Press F5 to execute.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Query] Schema cache failed: {ex.Message}");
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
        Tabs.Add(tab);
        ActiveTab = tab;
        SelectedTabIndex = Tabs.Count - 1;
        StatusMessage = $"New tab: {tab.Title}.";
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
    }

    private void CloseAllTabs()
    {
        foreach (var t in Tabs.ToList())
            Cancel(t);
        Tabs.Clear();
        NewTab();
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
        var cts = new CancellationTokenSource();
        tab.ExecutionCts = cts;
        tab.IsExecuting = true;
        tab.Results.Clear();
        tab.Messages.Clear();
        tab.HasResults = false;
        tab.HasMessages = false;
        tab.StatusMessage = "Executing…";
        tab.ElapsedText = string.Empty;
        ShowError = false;
        ErrorMessage = string.Empty;
        StatusMessage = $"Executing {tab.Title}…";
        var started = DateTime.UtcNow;

        try
        {
            var tables = await _service.ExecuteAsync(info, sql, ct: cts.Token);
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
            StatusMessage = $"{tab.Title}: {tab.StatusMessage}";
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

    private async Task CopyResultsAsync(QueryTab? tab)
    {
        if (tab == null || CopyToClipboardAsync == null) return;
        var first = tab.Results.FirstOrDefault(r => r.Rows.Count > 0);
        if (first == null)
        {
            tab.StatusMessage = "Nothing to copy — no result rows.";
            return;
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join("\t", first.Columns));
        foreach (var row in first.Rows.Take(1_000))
            sb.AppendLine(string.Join("\t", first.Columns.Select(c =>
                row.TryGetValue(c, out var v) ? v?.ToString() ?? "NULL" : "NULL")));
        await CopyToClipboardAsync(sb.ToString());
        tab.StatusMessage = $"Copied {Math.Min(first.Rows.Count, 1_000):N0} row(s) from “{first.Title}” (TSV).";
    }
}
