using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Controls;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.ViewModels;

/// <summary>
/// View-model for the DB Health window (SSMS Activity Monitor style).
/// A timer in the view triggers <see cref="RefreshTickAsync"/>; rates for
/// per-second perf counters are computed between samples, and client-side
/// history buffers feed the live charts. Tab data (waits, expensive queries,
/// deadlocks, file I/O) is refreshed on every third tick or on demand; its
/// sort/top/filter options re-slice the cached rows without hitting the server.
/// </summary>
public partial class DbHealthViewModel : ObservableObject
{
    private const int HistoryCapacity = 240;

    private readonly DbHealthService _service = new();
    private readonly SavedConnectionsService _savedService = new();
    private CancellationTokenSource _cts = new();
    private bool _inFlight;
    private int _tick;
    private DateTime _lastCounterUtc = DateTime.MinValue;
    private Dictionary<string, double> _prevCounters = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, double> _lastRates = new(StringComparer.OrdinalIgnoreCase);
    private List<HealthWaitRow> _waitsRaw = [];
    private List<HealthExpensiveQueryRow> _expensiveRaw = [];
    private List<HealthFileIoRow> _fileIoRaw = [];

    public ObservableCollection<SavedConnection> SavedConnections { get; } = [];
    [ObservableProperty] private SavedConnection? _selectedConnection;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _statusMessage = "Select a connection — monitoring starts automatically.";
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorMessage = "";

    // ── server info strip ──
    [ObservableProperty] private string _serverTitle = "Not connected";
    [ObservableProperty] private string _serverInfoText = "";
    [ObservableProperty] private string _uptimeText = "";

    // ── options ──
    public int[] IntervalOptions { get; } = [1, 2, 5, 10, 30];
    [ObservableProperty] private int _intervalSeconds = 5;
    [ObservableProperty] private bool _isPaused;
    public int[] WaitTopOptions { get; } = [10, 20, 30];
    [ObservableProperty] private int _waitTop = 10;
    public string[] ExpensiveSortOptions { get; } =
        ["Total CPU", "Avg CPU", "Total Duration", "Avg Duration", "Total Reads", "Executions"];
    [ObservableProperty] private int _expensiveSortIndex;
    public int[] ExpensiveTopOptions { get; } = [10, 25, 50];
    [ObservableProperty] private int _expensiveTop = 10;
    public ObservableCollection<string> DatabaseOptions { get; } = [];
    [ObservableProperty] private string? _processesFilter;
    [ObservableProperty] private bool _onlyBlocking;

    // ── KPI chips ──
    [ObservableProperty] private string _cpuNowText = "—";
    [ObservableProperty] private string _memoryNowText = "—";
    [ObservableProperty] private string _batchNowText = "—";
    [ObservableProperty] private string _connectionsNowText = "—";
    [ObservableProperty] private string _waitingNowText = "—";
    [ObservableProperty] private string _grantsNowText = "—";
    [ObservableProperty] private string _blockedNowText = "—";
    [ObservableProperty] private string _deadlocksNowText = "—";

    // ── chart series (new array per update so bindings re-render) ──
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _cpuSeries;
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _memorySeries;
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _batchSeries;
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _waitingSeries;
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _pageSplitsSeries;
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _pleSeries;
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _cacheHitSeries;
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _deadlockSeries;

    private readonly List<double> _cpuLive = [];
    private readonly List<double> _memCommitted = [];
    private readonly List<double> _memTarget = [];
    private readonly List<double> _batchRates = [];
    private readonly List<double> _waitingTasks = [];
    private readonly List<double> _runnableTasks = [];
    private readonly List<double> _pageSplits = [];
    private readonly List<double> _ple = [];
    private readonly List<double> _cacheHit = [];
    private readonly List<double> _deadlockRate = [];
    private readonly List<double> _lockWaits = [];

    // ── grids ──
    public ObservableCollection<HealthProcessRow> Processes { get; } = [];
    public ObservableCollection<HealthProcessRow> FilteredProcesses { get; } = [];
    public string ProcessesSummaryText => $"{FilteredProcesses.Count} of {Processes.Count} session(s) shown";
    public ObservableCollection<HealthWaitRow> Waits { get; } = [];
    [ObservableProperty] private string _waitsSummaryText = "";
    public ObservableCollection<HealthExpensiveQueryRow> ExpensiveQueries { get; } = [];
    [ObservableProperty] private string _expensiveSummaryText = "";
    public ObservableCollection<HealthDeadlockReport> Deadlocks { get; } = [];
    [ObservableProperty] private HealthDeadlockReport? _selectedDeadlock;
    public bool HasSelectedDeadlock => SelectedDeadlock != null;
    partial void OnSelectedDeadlockChanged(HealthDeadlockReport? value) => OnPropertyChanged(nameof(HasSelectedDeadlock));
    [ObservableProperty] private string _deadlocksSummaryText = "";
    public ObservableCollection<HealthFileIoRow> FileIo { get; } = [];
    [ObservableProperty] private string _fileIoSummaryText = "";
    [ObservableProperty] private string? _fileIoDatabaseFilter;
    private List<HealthMissingIndexRow> _missingRaw = [];
    public ObservableCollection<HealthMissingIndexRow> MissingIndexes { get; } = [];
    [ObservableProperty] private string _missingSummaryText = "";
    [ObservableProperty] private string? _missingDatabaseFilter;
    [ObservableProperty] private HealthMissingIndexRow? _selectedMissingIndex;
    public bool HasSelectedMissingIndex => SelectedMissingIndex != null;
    partial void OnSelectedMissingIndexChanged(HealthMissingIndexRow? value) =>
        OnPropertyChanged(nameof(HasSelectedMissingIndex));
    [ObservableProperty] private string _warningsText = "";

    // ── session actions ──
    [ObservableProperty] private HealthProcessRow? _selectedProcess;
    public bool HasSelectedProcess => SelectedProcess != null;
    public ObservableCollection<BlockingChainLink> BlockingChain { get; } = [];
    [ObservableProperty] private string _blockingChainText = "";
    public bool HasBlockingChain => BlockingChain.Count > 0;

    // ── error log ──
    public ObservableCollection<HealthErrorLogRow> ErrorLog { get; } = [];
    public int[] ErrorLogTailOptions { get; } = [200, 500, 1000, 2000];
    private List<HealthErrorLogRow> _errorLogRaw = [];
    [ObservableProperty] private int _errorLogTail = 500;
    [ObservableProperty] private string? _errorLogFilter;
    [ObservableProperty] private bool _errorLogOnlyErrors;
    [ObservableProperty] private string _errorLogSummaryText =
        "Not loaded. Reading the error log needs permission on the server log.";

    // ── Query Store (one database, on demand) ──
    [ObservableProperty] private string? _queryStoreDatabase;
    public int[] QueryStoreWindowOptions { get; } = [4, 12, 24, 48, 168, 720];
    [ObservableProperty] private int _queryStoreWindowHours = 24;
    /// <summary>Labels shown in the picker; the matching keys are what the service maps.</summary>
    public string[] QueryStoreRankOptions { get; } =
        ["Total duration", "Avg duration", "Total CPU", "Avg CPU", "Logical reads", "Executions"];
    private static readonly string[] QueryStoreRankKeys =
        ["duration", "avg_duration", "cpu", "avg_cpu", "reads", "executions"];
    [ObservableProperty] private int _queryStoreRankIndex;
    public ObservableCollection<QueryStoreRegressedRow> QueryStoreRegressed { get; } = [];
    public ObservableCollection<QueryStoreTopRow> QueryStoreTop { get; } = [];
    public ObservableCollection<QueryStorePlanRow> QueryStorePlans { get; } = [];
    [ObservableProperty] private QueryStoreRegressedRow? _selectedRegressed;
    [ObservableProperty] private QueryStoreTopRow? _selectedTop;
    [ObservableProperty] private QueryStorePlanRow? _selectedPlan;
    [ObservableProperty] private string _queryStoreStateText =
        "Pick a database and load its Query Store — it is off by default, and an off database has no history to show.";
    [ObservableProperty] private bool _queryStoreIsOn;
    [ObservableProperty] private string _queryStoreSummaryText = "";
    [ObservableProperty] private string _queryStoreQueryText = "";
    public bool HasQueryStorePlans => QueryStorePlans.Count > 0;

    public ICommand ConnectCommand { get; }
    public ICommand RefreshNowCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand RefreshDeadlocksCommand { get; }
    public ICommand CopyErrorCommand { get; }
    public ICommand CopyMissingIndexScriptCommand { get; }
    public ICommand ShowBlockingChainCommand { get; }
    public ICommand KillSessionCommand { get; }
    public ICommand LoadErrorLogCommand { get; }
    public ICommand LoadQueryStoreCommand { get; }
    public ICommand ForcePlanCommand { get; }
    public ICommand UnforcePlanCommand { get; }
    public ICommand EnableQueryStoreCommand { get; }

    /// <summary>Set by the view to enable copying the error text.</summary>
    public Func<string, Task<bool>>? CopyToClipboardAsync { get; set; }

    /// <summary>
    /// Set by the view to confirm a statement before it runs. Left null the window
    /// refuses destructive actions rather than guessing on the operator's behalf.
    /// </summary>
    public Func<string, string, string, Task<bool>>? ConfirmScriptAsync { get; set; }

    public DbHealthViewModel()
    {
        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        RefreshNowCommand = new AsyncRelayCommand(() => RefreshTickAsync(forceSlow: true));
        TogglePauseCommand = new RelayCommand(() => IsPaused = !IsPaused);
        RefreshDeadlocksCommand = new AsyncRelayCommand(RefreshDeadlocksAsync);
        CopyErrorCommand = new AsyncRelayCommand(CopyErrorAsync);
        CopyMissingIndexScriptCommand = new AsyncRelayCommand(CopyMissingIndexScriptAsync);
        ShowBlockingChainCommand = new RelayCommand(ShowBlockingChain);
        KillSessionCommand = new AsyncRelayCommand(KillSessionAsync);
        LoadErrorLogCommand = new AsyncRelayCommand(LoadErrorLogAsync);
        LoadQueryStoreCommand = new AsyncRelayCommand(LoadQueryStoreAsync);
        ForcePlanCommand = new AsyncRelayCommand(ForcePlanAsync);
        UnforcePlanCommand = new AsyncRelayCommand(UnforcePlanAsync);
        EnableQueryStoreCommand = new AsyncRelayCommand(EnableQueryStoreAsync);

        foreach (var c in _savedService.Load())
            SavedConnections.Add(c);
        SelectedConnection = SavedConnections.FirstOrDefault();
        DatabaseOptions.Add("(All databases)");
    }

    // ═══════════ connect / refresh pipeline ═══════════

    private async Task ConnectAsync()
    {
        if (SelectedConnection == null)
        {
            StatusMessage = "Select a saved connection first.";
            return;
        }
        ShowError = false;
        StatusMessage = $"Connecting to {SelectedConnection.DisplayName}…";
        ClearHistory();
        _tick = 0;
        _lastCounterUtc = DateTime.MinValue;
        _prevCounters.Clear();
        _lastRates.Clear();
        _cts.Cancel();
        // A refresh may already be running (e.g. auto-start on window open);
        // wait for it to observe cancellation instead of dropping this connect.
        for (var i = 0; i < 100 && _inFlight; i++)
            await Task.Delay(20);
        _cts = new CancellationTokenSource();
        IsConnected = false;
        await RefreshTickAsync(forceSlow: true);
    }

    public async Task RefreshTickAsync(bool forceSlow)
    {
        if (SelectedConnection == null)
            return;
        if (_inFlight)
            return;
        _inFlight = true;
        IsRefreshing = true;
        _tick++;
        var slow = forceSlow || _tick % 3 == 1 || !IsConnected;
        try
        {
            var info = SelectedConnection.ToConnectionInfo();
            var snap = await _service.CollectAsync(info, slow, _cts.Token);
            Apply(snap, slow);
            ShowError = false;
            StatusMessage = $"Updated {DateTime.Now:HH:mm:ss} • interval {IntervalSeconds}s{(IsPaused ? " (paused)" : "")}";
        }
        catch (OperationCanceledException)
        {
            // re-connect or shutdown — ignore
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ServerTitle = "Connection lost";
            ShowError = true;
            ErrorMessage = ex.Message;
            StatusMessage = "Health refresh failed — will retry next tick.";
        }
        finally
        {
            _inFlight = false;
            IsRefreshing = false;
        }
    }

    private async Task RefreshDeadlocksAsync()
    {
        if (!IsConnected) return;
        await RefreshTickAsync(forceSlow: true);
    }

    // ═══════════ apply snapshot ═══════════

    private void Apply(DbHealthSnapshot snap, bool slow)
    {
        IsConnected = true;
        WarningsText = snap.Warnings.Count > 0 ? "Unavailable: " + string.Join(" • ", snap.Warnings) : "";

        if (snap.ServerInfo is { } si)
        {
            ServerTitle = $"{si.ServerName} — {si.ProductLevel}";
            ServerInfoText = $"{si.ProductVersion} • {si.CpuCount} CPU ({si.SchedulerCount} schedulers) • {FormatKb(si.PhysicalMemoryKb)} RAM";
            UptimeText = si.SqlServerStartTimeUtc is { } st
                ? $"Uptime: {FormatSpan(DateTime.UtcNow - st)}"
                : "";
        }

        // Rates for cumulative per-second counters. A pass <0.2 s after the
        // previous one cannot produce rates — keep the last known values so a
        // manual refresh does not blank KPIs or push zeros into the charts.
        var rates = ComputeRates(snap.Counters);
        if (rates.Count > 0) _lastRates = rates;
        else rates = _lastRates;
        _prevCounters = snap.Counters.Values;
        _lastCounterUtc = snap.Counters.Utc;

        // KPI chips
        CpuNowText = $"{snap.SqlCpuPercent:0} %";
        MemoryNowText = FormatKb(snap.ProcessPhysicalKb);
        BatchNowText = rates.TryGetValue("Batch Requests/sec", out var b) ? $"{b:0.#}/s" : "—";
        ConnectionsNowText = snap.Counters.Values.TryGetValue("User Connections", out var uc) ? uc.ToString("N0") : "—";
        WaitingNowText = $"{snap.WaitingTasks:N0} (+{snap.RunnableTasks:N0} runnable)";
        GrantsNowText = snap.Counters.Values.TryGetValue("Memory Grants Pending", out var mg) ? mg.ToString("N0") : "—";
        BlockedNowText = snap.Counters.Values.TryGetValue("Processes blocked", out var pb) ? pb.ToString("N0") : "0";
        DeadlocksNowText = rates.TryGetValue("Number of Deadlocks/sec", out var dr) ? $"{dr:0.##}/s" : "0/s";

        // History buffers
        Push(_cpuLive, snap.SqlCpuPercent);
        Push(_memCommitted, snap.CommittedKb / 1024.0);
        Push(_memTarget, snap.CommittedTargetKb / 1024.0);
        Push(_batchRates, rates.GetValueOrDefault("Batch Requests/sec"));
        Push(_waitingTasks, snap.WaitingTasks);
        Push(_runnableTasks, snap.RunnableTasks);
        Push(_pageSplits, rates.GetValueOrDefault("Page Splits/sec"));
        if (snap.Counters.Values.TryGetValue("Page life expectancy", out var ple)) Push(_ple, ple);
        if (snap.Counters.Values.TryGetValue("Buffer cache hit ratio", out var chr) &&
            snap.Counters.Values.TryGetValue("Buffer cache hit ratio base", out var baseV) && baseV > 0)
            Push(_cacheHit, chr / baseV * 100.0);
        Push(_deadlockRate, rates.GetValueOrDefault("Number of Deadlocks/sec"));
        Push(_lockWaits, rates.GetValueOrDefault("Lock Waits/sec"));

        // Charts
        CpuSeries =
        [
            new ChartSeries
            {
                Points = [.. snap.CpuHistory.Select(c => c.SqlPercent)],
                Stroke = Brush("#7C6FF0"), Label = "history", Fill = true
            },
            new ChartSeries { Points = [.. _cpuLive], Stroke = Brush("#6FCF97"), Label = "live" }
        ];
        MemorySeries =
        [
            new ChartSeries { Points = [.. _memCommitted], Stroke = Brush("#7C6FF0"), Label = "committed", Fill = true },
            new ChartSeries { Points = [.. _memTarget], Stroke = Brush("#E5B567"), Label = "target" }
        ];
        BatchSeries = [new ChartSeries { Points = [.. _batchRates], Stroke = Brush("#4FB3BF"), Label = "Batch Requests", Fill = true }];
        WaitingSeries =
        [
            new ChartSeries { Points = [.. _waitingTasks], Stroke = Brush("#E5B567"), Label = "queued", Fill = true },
            new ChartSeries { Points = [.. _runnableTasks], Stroke = Brush("#6FCF97"), Label = "runnable" }
        ];
        PageSplitsSeries = [new ChartSeries { Points = [.. _pageSplits], Stroke = Brush("#C792EA"), Label = "Page Splits", Fill = true }];
        PleSeries = [new ChartSeries { Points = [.. _ple], Stroke = Brush("#4FB3BF"), Label = "PLE", Fill = true }];
        CacheHitSeries = [new ChartSeries { Points = [.. _cacheHit], Stroke = Brush("#6FCF97"), Label = "hit ratio", Fill = true }];
        DeadlockSeries =
        [
            new ChartSeries { Points = [.. _deadlockRate], Stroke = Brush("#EF6B73"), Label = "Deadlocks/s", Fill = true },
            new ChartSeries { Points = [.. _lockWaits], Stroke = Brush("#E5B567"), Label = "Lock Waits/s" }
        ];

        // Process grid (fast section — every tick). The rows are new objects each
        // tick, so re-match the selection by session id to keep it stable.
        var keepSession = SelectedProcess?.SessionId;
        Processes.Clear();
        foreach (var p in snap.Processes) Processes.Add(p);
        if (keepSession is { } spid)
            SelectedProcess = Processes.FirstOrDefault(p => p.SessionId == spid);
        ApplyProcessFilter();

        if (slow)
        {
            if (snap.Waits != null)
            {
                _waitsRaw = snap.Waits;
                RebuildWaits();
            }
            if (snap.ExpensiveQueries != null)
            {
                _expensiveRaw = snap.ExpensiveQueries;
                RebuildExpensive();
            }
            if (snap.FileIo != null)
            {
                _fileIoRaw = snap.FileIo;
                RebuildFileIo();
            }
            if (snap.MissingIndexes != null)
            {
                _missingRaw = snap.MissingIndexes;
                RebuildMissing();
            }
            if (snap.Deadlocks != null)
            {
                var keep = SelectedDeadlock;
                Deadlocks.Clear();
                foreach (var d in snap.Deadlocks) Deadlocks.Add(d);
                SelectedDeadlock = keep != null
                    ? Deadlocks.FirstOrDefault(d => SameDeadlock(d, keep))
                    : Deadlocks.FirstOrDefault();
                DeadlocksSummaryText = Deadlocks.Count == 0
                    ? "No deadlocks in the system_health ring buffer (~4 h window)."
                    : $"{Deadlocks.Count} deadlock(s) recorded (most recent first).";
            }
            if (snap.Databases != null)
            {
                var current = FileIoDatabaseFilter ?? "(All databases)";
                DatabaseOptions.Clear();
                DatabaseOptions.Add("(All databases)");
                foreach (var db in snap.Databases) DatabaseOptions.Add(db);
                FileIoDatabaseFilter = DatabaseOptions.Contains(current) ? current : "(All databases)";
            }
        }
    }

    private static void Push(List<double> list, double value)
    {
        list.Add(value);
        if (list.Count > HistoryCapacity)
            list.RemoveRange(0, list.Count - HistoryCapacity);
    }

    private static bool SameDeadlock(HealthDeadlockReport a, HealthDeadlockReport b) =>
        a.TimeUtc == b.TimeUtc && a.VictimSpid == b.VictimSpid && a.Processes.Count == b.Processes.Count;

    private Dictionary<string, double> ComputeRates(HealthCounterSample sample)
    {
        var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var dt = (sample.Utc - _lastCounterUtc).TotalSeconds;
        if (_lastCounterUtc == DateTime.MinValue || dt < 0.2)
            return rates;
        foreach (var (name, value) in sample.Values)
        {
            if (!PerSecondCounters.Contains(name)) continue;
            if (!_prevCounters.TryGetValue(name, out var prev)) continue;
            var rate = (value - prev) / dt;
            if (rate >= 0)
                rates[name] = rate;
        }
        return rates;
    }

    private static readonly HashSet<string> PerSecondCounters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Batch Requests/sec", "SQL Re-Compilations/sec", "Transactions/sec",
        "Page Splits/sec", "Full Scans/sec", "Forwarded Records/sec",
        "Number of Deadlocks/sec", "Lock Waits/sec", "Lock Timeouts/sec",
        "Checkpoint pages/sec", "Errors/sec"
    };

    // ═══════════ client-side grid options ═══════════

    partial void OnProcessesFilterChanged(string? value) => ApplyProcessFilter();
    partial void OnOnlyBlockingChanged(bool value) => ApplyProcessFilter();

    private void ApplyProcessFilter()
    {
        FilteredProcesses.Clear();
        var filter = ProcessesFilter?.Trim();
        foreach (var p in Processes)
        {
            if (OnlyBlocking && !p.IsBlocked) continue;
            if (!string.IsNullOrEmpty(filter) &&
                !(p.Database.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                  p.Login.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                  p.Program.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                  p.Command.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                  p.SessionId.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                  p.Statement.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                continue;
            FilteredProcesses.Add(p);
        }
        OnPropertyChanged(nameof(ProcessesSummaryText));
    }

    partial void OnWaitTopChanged(int value) => RebuildWaits();

    private void RebuildWaits()
    {
        var total = _waitsRaw.Sum(w => w.WaitSeconds);
        Waits.Clear();
        foreach (var w in _waitsRaw.Take(WaitTop))
        {
            w.SharePercent = total > 0 ? w.WaitSeconds / total * 100.0 : 0;
            Waits.Add(w);
        }
        WaitsSummaryText = Waits.Count == 0
            ? "No wait data."
            : $"Top {Waits.Count} of {_waitsRaw.Count} wait types (share of the top list).";
    }

    partial void OnExpensiveSortIndexChanged(int value) => RebuildExpensive();
    partial void OnExpensiveTopChanged(int value) => RebuildExpensive();

    private void RebuildExpensive()
    {
        IEnumerable<HealthExpensiveQueryRow> ordered = ExpensiveSortIndex switch
        {
            1 => _expensiveRaw.OrderByDescending(q => q.AvgCpuMs),
            2 => _expensiveRaw.OrderByDescending(q => q.TotalElapsedMs),
            3 => _expensiveRaw.OrderByDescending(q => q.AvgElapsedMs),
            4 => _expensiveRaw.OrderByDescending(q => q.TotalReads),
            5 => _expensiveRaw.OrderByDescending(q => q.ExecutionCount),
            _ => _expensiveRaw
        };
        ExpensiveQueries.Clear();
        foreach (var q in ordered.Take(ExpensiveTop))
            ExpensiveQueries.Add(q);
        ExpensiveSummaryText = ExpensiveQueries.Count == 0
            ? "No cached query stats yet (sys.dm_exec_query_stats is empty after a restart)."
            : $"Top {ExpensiveQueries.Count} by {ExpensiveSortOptions[Math.Clamp(ExpensiveSortIndex, 0, ExpensiveSortOptions.Length - 1)]} (from up to {_expensiveRaw.Count} cached statements).";
    }

    partial void OnFileIoDatabaseFilterChanged(string? value) => RebuildFileIo();

    private void RebuildFileIo()
    {
        FileIo.Clear();
        var want = FileIoDatabaseFilter;
        foreach (var f in _fileIoRaw)
        {
            if (want is not (null or "(All databases)") && !string.Equals(f.Database, want, StringComparison.OrdinalIgnoreCase))
                continue;
            FileIo.Add(f);
        }
        FileIoSummaryText = $"{FileIo.Count} file(s), heaviest total I/O stalls first.";
    }

    partial void OnMissingDatabaseFilterChanged(string? value) => RebuildMissing();

    private void RebuildMissing()
    {
        MissingIndexes.Clear();
        var want = MissingDatabaseFilter;
        foreach (var m in _missingRaw)
        {
            if (want is not (null or "(All databases)") && !string.Equals(m.Database, want, StringComparison.OrdinalIgnoreCase))
                continue;
            MissingIndexes.Add(m);
        }
        MissingSummaryText = MissingIndexes.Count == 0
            ? "No missing-index recommendations (DMVs reset on restart)."
            : $"{MissingIndexes.Count} recommendation(s), most effective first — impact × uses (SSMS details-report ranking).";
    }

    private async Task CopyMissingIndexScriptAsync()
    {
        if (SelectedMissingIndex is not { } row || CopyToClipboardAsync is null)
            return;
        if (string.IsNullOrEmpty(row.CreateScript))
        {
            StatusMessage = "No script for this recommendation.";
            return;
        }
        await CopyToClipboardAsync(row.CreateScript);
        StatusMessage = $"CREATE INDEX script copied — {row.Table} ({row.ImpactText} impact).";
    }

    // ═══════════ session actions: blocking chain + KILL ═══════════

    partial void OnSelectedProcessChanged(HealthProcessRow? value)
    {
        OnPropertyChanged(nameof(HasSelectedProcess));
        if (value == null)
        {
            BlockingChain.Clear();
            BlockingChainText = "";
            OnPropertyChanged(nameof(HasBlockingChain));
        }
    }

    /// <summary>
    /// Walks blocking_session_id from the selected session up to the head blocker.
    /// Read from the cached Processes sample, so it never touches the server.
    /// </summary>
    private void ShowBlockingChain()
    {
        if (SelectedProcess is not { } row)
        {
            StatusMessage = "Select a session in the grid to trace its blocker.";
            return;
        }

        var chain = DbHealthService.DescribeBlockingChain(Processes, row.SessionId);
        BlockingChain.Clear();
        foreach (var link in chain)
            BlockingChain.Add(link);
        OnPropertyChanged(nameof(HasBlockingChain));

        var head = chain.Count > 0 ? chain[0] : null;
        var heldByHead = head == null ? 0 : Processes.Count(p => p.BlockingSessionId == head.SessionId);
        BlockingChainText = chain.Count switch
        {
            0 => $"Session {row.SessionId} was not in the last sample, so its blocker cannot be traced.",
            1 when heldByHead > 0 =>
                $"Session {row.SessionId} is not itself blocked, but {heldByHead} session(s) wait on it.",
            1 => $"Session {row.SessionId} is not blocked by another session.",
            _ => $"{chain.Count}-session chain: head blocker spid {head!.SessionId} " +
                 $"({head.Database}/{head.Login}, {heldByHead} direct victim(s)) blocks through to spid {row.SessionId}."
        };
        StatusMessage = chain.Count > 1
            ? $"Blocking chain for session {row.SessionId} traced back to session {head!.SessionId}."
            : BlockingChainText;
    }

    private async Task KillSessionAsync()
    {
        if (SelectedConnection == null || SelectedProcess is not { } row)
        {
            StatusMessage = "Select a session in the grid before using Kill session.";
            return;
        }
        if (ConfirmScriptAsync == null)
        {
            StatusMessage = "Kill needs a confirmation host, and this window has none attached.";
            return;
        }

        var script = $"KILL {row.SessionId};";
        var warning =
            $"KILL ends session {row.SessionId} ({row.Login} @ {row.Host}, database {row.Database}) and rolls back " +
            "everything it was doing. An open transaction is undone — that can take far longer than the original work — " +
            "and its uncommitted changes are lost.";
        if (row.IsBlocked)
            warning += $"\nThis session is itself waiting on session {row.BlockingSessionId}.";
        else
        {
            var victims = Processes.Count(p => p.BlockingSessionId == row.SessionId);
            if (victims > 0)
                warning += $"\n⚠ {victims} other session(s) are waiting on it; killing it frees them.";
        }

        if (!await ConfirmScriptAsync("Kill session", warning, script))
        {
            StatusMessage = $"Kill of session {row.SessionId} cancelled.";
            return;
        }

        try
        {
            var info = SelectedConnection.ToConnectionInfo();
            await _service.KillSessionAsync(info, row.SessionId, _cts.Token);
            AppLog.Info($"KILL {row.SessionId} ({row.Login}@{row.Host}, {row.Database}) issued from DB Health.");
            StatusMessage = $"✓ KILL {row.SessionId} accepted — refresh to confirm the session is gone.";
            BlockingChain.Clear();
            OnPropertyChanged(nameof(HasBlockingChain));
            await RefreshTickAsync(forceSlow: false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowError = true;
            ErrorMessage = ex.Message;
            AppLog.Error("DbHealthViewModel", ex, $"KILL {row.SessionId} failed");
            StatusMessage = $"KILL {row.SessionId} failed — see the error banner.";
        }
    }

    // ═══════════ error log ═══════════

    partial void OnErrorLogOnlyErrorsChanged(bool value) => RebuildErrorLog();

    private async Task LoadErrorLogAsync()
    {
        if (SelectedConnection == null)
        {
            StatusMessage = "Connect to a server before loading its error log.";
            return;
        }
        try
        {
            var info = SelectedConnection.ToConnectionInfo();
            var filter = ErrorLogFilter?.Trim();
            StatusMessage = $"Reading the last {ErrorLogTail} error-log record(s){(string.IsNullOrEmpty(filter) ? "" : $" matching “{filter}”")}…";
            _errorLogRaw = await _service.ReadErrorLogAsync(info, ErrorLogTail, filter, _cts.Token);
            RebuildErrorLog();
            ShowError = false;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorLogSummaryText = $"Error log could not be read from {SelectedConnection.DisplayName}.";
            ShowError = true;
            ErrorMessage = ex.Message;
            AppLog.Error("DbHealthViewModel", ex, "Error log read failed");
            StatusMessage = "Error log read failed — see the error banner.";
        }
    }

    private void RebuildErrorLog()
    {
        ErrorLog.Clear();
        foreach (var row in _errorLogRaw)
        {
            if (ErrorLogOnlyErrors && !row.IsError) continue;
            ErrorLog.Add(row);
        }
        if (_errorLogRaw.Count == 0)
        {
            ErrorLogSummaryText = ErrorLogFilter is { Length: > 0 } f
                ? $"No error-log records match “{f}” in the last {ErrorLogTail}."
                : "The error log returned no records.";
            return;
        }
        var errors = _errorLogRaw.Count(r => r.IsError);
        ErrorLogSummaryText = ErrorLogOnlyErrors
            ? $"{ErrorLog.Count} record(s) of severity 11+ shown from {_errorLogRaw.Count} read."
            : $"{ErrorLog.Count} of {_errorLogRaw.Count} record(s) shown • {errors} at severity 11+.";
    }

    private void ClearHistory()
    {
        Processes.Clear();
        FilteredProcesses.Clear();
        Waits.Clear();
        ExpensiveQueries.Clear();
        Deadlocks.Clear();
        FileIo.Clear();
        MissingIndexes.Clear();
        BlockingChain.Clear();
        BlockingChainText = "";
        ErrorLog.Clear();
        _errorLogRaw = [];
        ErrorLogSummaryText = "Not loaded. Reading the error log needs permission on the server log.";
        QueryStoreRegressed.Clear();
        QueryStoreTop.Clear();
        QueryStorePlans.Clear();
        SelectedRegressed = null;
        SelectedTop = null;
        SelectedPlan = null;
        QueryStoreIsOn = false;
        QueryStoreQueryText = "";
        QueryStoreSummaryText = "";
        QueryStoreStateText = "Pick a database and load its Query Store — it is off by default, " +
                              "and an off database has no history to show.";
        _waitsRaw = [];
        _expensiveRaw = [];
        _fileIoRaw = [];
        _missingRaw = [];
        _cpuLive.Clear(); _memCommitted.Clear(); _memTarget.Clear(); _batchRates.Clear();
        _waitingTasks.Clear(); _runnableTasks.Clear(); _pageSplits.Clear();
        _ple.Clear(); _cacheHit.Clear(); _deadlockRate.Clear(); _lockWaits.Clear();
    }

    public void Shutdown()
    {
        _cts.Cancel();
        _service.Dispose();
    }

    // ═══════════ Query Store ═══════════

    /// <summary>How much worse the second half of the window has to be before a query is
    /// called regressed. SSMS's report has the same knob; 20 % keeps noise out.</summary>
    public double[] QueryStoreChangeOptions { get; } = [5, 10, 20, 50];
    [ObservableProperty] private double _queryStoreMinChange = 20;

    /// <summary>The database the tab reads. "(All databases)" filters the other grids;
    /// Query Store is a per-database store, so the tab falls back to the database the
    /// connection names rather than guessing across all of them.</summary>
    private ConnectionInfo? QueryStoreTarget()
    {
        if (SelectedConnection == null) return null;
        var db = string.IsNullOrWhiteSpace(QueryStoreDatabase) || QueryStoreDatabase == "(All databases)"
            ? SelectedConnection.Database
            : QueryStoreDatabase;
        return string.IsNullOrWhiteSpace(db) ? null : SelectedConnection.ToConnectionInfo().ForDatabase(db);
    }

    private long? SelectedQueryId => SelectedRegressed?.QueryId ?? SelectedTop?.QueryId;

    private async Task LoadQueryStoreAsync()
    {
        if (SelectedConnection == null)
        {
            StatusMessage = "Connect to a server before reading its Query Store.";
            return;
        }
        var target = QueryStoreTarget();
        if (target == null)
        {
            QueryStoreIsOn = false;
            QueryStoreStateText = "Query Store belongs to one database — name the database to read.";
            return;
        }

        try
        {
            IsRefreshing = true;
            StatusMessage = $"Reading Query Store for [{target.Database}]…";
            var state = await _service.GetQueryStoreStateAsync(target, _cts.Token);
            QueryStoreStateText = state.Summary;
            QueryStoreIsOn = state.IsOn;
            QueryStoreRegressed.Clear();
            QueryStoreTop.Clear();
            QueryStorePlans.Clear();
            SelectedPlan = null;
            QueryStoreQueryText = "";

            if (!state.IsOn)
            {
                QueryStoreSummaryText =
                    "Nothing was captured, so there is nothing to rank. Enabling Query Store collects from " +
                    "now on — it cannot recover what happened before.";
                StatusMessage = $"Query Store is {state.ActualState} on [{target.Database}].";
                return;
            }

            var rank = QueryStoreRankIndex >= 0 && QueryStoreRankIndex < QueryStoreRankKeys.Length
                ? QueryStoreRankKeys[QueryStoreRankIndex] : "duration";
            var regressed = await _service.GetRegressedQueriesAsync(
                target, QueryStoreWindowHours, QueryStoreMinChange, 1, 50, _cts.Token);
            var top = await _service.GetTopQueriesAsync(
                target, QueryStoreWindowHours, rank, 50, _cts.Token);
            foreach (var r in regressed) QueryStoreRegressed.Add(r);
            foreach (var t in top) QueryStoreTop.Add(t);

            QueryStoreSummaryText =
                $"{regressed.Count} of {top.Count} query/plan pair(s) in the last {QueryStoreWindowHours} h got more than " +
                $"{QueryStoreMinChange:0.#}% slower between the first and the second half of the window. " +
                $"Ranked by {QueryStoreRankOptions[Math.Clamp(QueryStoreRankIndex, 0, QueryStoreRankOptions.Length - 1)]}.";
            StatusMessage = $"Query Store — {top.Count} ranked query/plan pair(s) on [{target.Database}].";
            AppLog.Info($"Query Store read for [{target.Database}]: {state.ActualState}, " +
                        $"{regressed.Count} regressed, {top.Count} ranked.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowError = true;
            ErrorMessage = ex.Message;
            AppLog.Error("DbHealthViewModel", ex, $"Query Store load for [{target.Database}] failed");
            QueryStoreSummaryText = $"Query Store could not be read on [{target.Database}].";
            StatusMessage = "Query Store failed — see the error banner.";
        }
        finally
        {
            IsRefreshing = false;
            // The plan pane was emptied at the top of the load, so its "select a query"
            // hint has to be re-evaluated on every path out of here, not only when the
            // store is off — otherwise it keeps a stale "plans are showing" value.
            OnPropertyChanged(nameof(HasQueryStorePlans));
        }
    }

    partial void OnSelectedRegressedChanged(QueryStoreRegressedRow? value)
    {
        if (value == null) return;
        SelectedTop = null;
        QueryStoreQueryText = value.QueryText;
        _ = LoadPlansAsync(value.QueryId);
    }

    partial void OnSelectedTopChanged(QueryStoreTopRow? value)
    {
        if (value == null) return;
        SelectedRegressed = null;
        QueryStoreQueryText = value.QueryText;
        _ = LoadPlansAsync(value.QueryId);
    }

    private async Task LoadPlansAsync(long queryId)
    {
        var target = QueryStoreTarget();
        if (target == null) return;
        try
        {
            QueryStorePlans.Clear();
            SelectedPlan = null;
            var plans = await _service.GetQueryPlansAsync(target, queryId, QueryStoreWindowHours, _cts.Token);
            foreach (var p in plans) QueryStorePlans.Add(p);
            SelectedPlan = plans.FirstOrDefault(p => !p.IsForced) ?? plans.FirstOrDefault();
            QueryStoreSummaryText = plans.Count switch
            {
                > 1 => $"{plans.Count} plans are stored for query {queryId}. Forcing pins one of them; " +
                       "the server stops recompiling this query until the plan is released.",
                1 => $"Query {queryId} has one plan ({plans[0].StatusText}).",
                _ => $"Query {queryId} has no plans.",
            };
            OnPropertyChanged(nameof(HasQueryStorePlans));
        }
        catch (Exception ex)
        {
            AppLog.Error("DbHealthViewModel", ex, $"Query Store plans of {queryId}");
            QueryStoreSummaryText = $"The plans of query {queryId} could not be read.";
        }
    }

    private async Task ForcePlanAsync()
    {
        var target = QueryStoreTarget();
        if (target == null || SelectedQueryId is not { } queryId || SelectedPlan is not { } plan)
        {
            StatusMessage = "Select a query in either grid, then the plan to pin.";
            return;
        }
        var script = ManagerScriptBuilder.ForceQueryPlan(queryId, plan.PlanId);
        var warning =
            $"Pins plan {plan.PlanId} to query {queryId} in [{target.Database}]. The optimiser has to use this plan " +
            "for every execution until it is released, so a bad choice degrades the whole workload, and the pin " +
            "survives a restart. Forcing fails silently in the sense that the server records the reason instead of " +
            "applying it — check the plan list afterwards.";
        await RunQueryStoreScriptAsync("Force plan", warning, script, target, queryId);
    }

    private async Task UnforcePlanAsync()
    {
        var target = QueryStoreTarget();
        if (target == null || SelectedQueryId is not { } queryId || SelectedPlan is not { } plan)
        {
            StatusMessage = "Select a query in either grid, then the forced plan to release.";
            return;
        }
        if (!plan.IsForced)
        {
            StatusMessage = $"Plan {plan.PlanId} of query {queryId} is not forced — there is nothing to release.";
            return;
        }
        var script = ManagerScriptBuilder.UnforceQueryPlan(queryId, plan.PlanId);
        var warning =
            $"Releases the pinned plan of query {queryId} in [{target.Database}]. The next execution recompiles, " +
            "and the optimiser picks a plan again — which is the point, unless the pinned plan was holding a " +
            "regression back.";
        await RunQueryStoreScriptAsync("Unforce plan", warning, script, target, queryId);
    }

    private async Task EnableQueryStoreAsync()
    {
        var target = QueryStoreTarget();
        if (target == null)
        {
            StatusMessage = "Name the database to enable Query Store on.";
            return;
        }
        string script;
        try
        {
            script = ManagerScriptBuilder.EnableQueryStore(target.Database);
        }
        catch (Exception ex)
        {
            ShowError = true;
            ErrorMessage = ex.Message;
            return;
        }
        var warning =
            $"Turns Query Store on for [{target.Database}]: runtime statistics for every query are kept " +
            "(up to 1024 MB, cleaned by size), flushed every 15 minutes, and it adds work to compile and " +
            "capture. It collects from now on and recovers nothing from before.";
        await RunQueryStoreScriptAsync("Enable Query Store", warning, script, target, queryId: null);
    }

    /// <summary>Confirmation first, then the exact script that was shown, then a reload of
    /// what changed. With no confirmation host attached nothing is attempted.</summary>
    private async Task RunQueryStoreScriptAsync(
        string title, string warning, string script, ConnectionInfo target, long? queryId)
    {
        if (ConfirmScriptAsync == null)
        {
            StatusMessage = $"{title} needs a confirmation host, and this window has none attached.";
            return;
        }

        if (!await ConfirmScriptAsync(title, warning, script))
        {
            StatusMessage = $"{title} cancelled — Query Store was not touched.";
            return;
        }

        try
        {
            IsRefreshing = true;
            await _service.RunQueryStoreScriptAsync(target, script, _cts.Token);
            AppLog.Info($"{title} on [{target.Database}] issued from DB Health.");
            StatusMessage = $"✓ {title} applied to [{target.Database}].";
            if (queryId is { } qid)
            {
                var keep = SelectedPlan?.PlanId ?? 0;
                await LoadPlansAsync(qid);
                SelectedPlan = QueryStorePlans.FirstOrDefault(p => p.PlanId == keep) ?? SelectedPlan;
            }
            else
            {
                await LoadQueryStoreAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowError = true;
            ErrorMessage = ex.Message;
            AppLog.Error("DbHealthViewModel", ex, $"{title} on [{target.Database}] failed");
            StatusMessage = $"{title} failed — see the error banner.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    // ═══════════ misc ═══════════

    private async Task CopyErrorAsync()
    {
        if (CopyToClipboardAsync != null && !string.IsNullOrEmpty(ErrorMessage))
            await CopyToClipboardAsync(ErrorMessage);
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static string FormatKb(long kb) =>
        kb >= 1024 * 1024 ? $"{kb / 1024.0 / 1024.0:0.#} GB" : $"{kb / 1024.0:0} MB";

    private static string FormatSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{(int)span.TotalMinutes}m";
    }
}
