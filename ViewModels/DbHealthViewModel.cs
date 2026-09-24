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
    public string ProcessesSummaryText => $"{FilteredProcesses.Count} of {Processes.Count} active request(s) shown";
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
    [ObservableProperty] private string _warningsText = "";

    public ICommand ConnectCommand { get; }
    public ICommand RefreshNowCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand RefreshDeadlocksCommand { get; }
    public ICommand CopyErrorCommand { get; }

    /// <summary>Set by the view to enable copying the error text.</summary>
    public Func<string, Task<bool>>? CopyToClipboardAsync { get; set; }

    public DbHealthViewModel()
    {
        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        RefreshNowCommand = new AsyncRelayCommand(() => RefreshTickAsync(forceSlow: true));
        TogglePauseCommand = new RelayCommand(() => IsPaused = !IsPaused);
        RefreshDeadlocksCommand = new AsyncRelayCommand(RefreshDeadlocksAsync);
        CopyErrorCommand = new AsyncRelayCommand(CopyErrorAsync);

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

        // Process grid (fast section — every tick)
        Processes.Clear();
        foreach (var p in snap.Processes) Processes.Add(p);
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

    private void ClearHistory()
    {
        Processes.Clear();
        FilteredProcesses.Clear();
        Waits.Clear();
        ExpensiveQueries.Clear();
        Deadlocks.Clear();
        FileIo.Clear();
        _waitsRaw = [];
        _expensiveRaw = [];
        _fileIoRaw = [];
        _cpuLive.Clear(); _memCommitted.Clear(); _memTarget.Clear(); _batchRates.Clear();
        _waitingTasks.Clear(); _runnableTasks.Clear(); _pageSplits.Clear();
        _ple.Clear(); _cacheHit.Clear(); _deadlockRate.Clear(); _lockWaits.Clear();
    }

    public void Shutdown() => _cts.Cancel();

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
