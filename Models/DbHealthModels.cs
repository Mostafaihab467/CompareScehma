namespace SchemaCompare.Models;

using Avalonia.Media;

/// <summary>One point of SQL Server CPU utilization history (from the scheduler
/// monitor ring buffer — the same source SSMS Activity Monitor graphs use).</summary>
public sealed class CpuSample
{
    public DateTime Time { get; init; }
    public double SqlPercent { get; init; }
    public double IdlePercent { get; init; }
    public double OtherPercent { get; init; }
}

public sealed class HealthServerInfo
{
    public string ServerName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public string ProductLevel { get; init; } = "";
    public DateTime? SqlServerStartTimeUtc { get; init; }
    public int CpuCount { get; init; }
    public int HyperthreadRatio { get; init; }
    public int SchedulerCount { get; init; }
    public long PhysicalMemoryKb { get; init; }
}

/// <summary>A SQL Server perf counter sample. Per-second counters (cntr_type
/// 272696576) are cumulative; the rate between two samples is computed by the VM.</summary>
public sealed class HealthCounterSample
{
    public DateTime Utc { get; init; }
    public required Dictionary<string, double> Values { get; init; }
}

public sealed class HealthProcessRow
{
    public int SessionId { get; init; }
    public int BlockingSessionId { get; init; }
    public string Database { get; init; } = "";
    public string Status { get; init; } = "";
    public string Command { get; init; } = "";
    public string WaitType { get; init; } = "";
    public long WaitMs { get; init; }
    public long CpuMs { get; init; }
    public long LogicalReads { get; init; }
    public long Reads { get; init; }
    public long Writes { get; init; }
    public long ElapsedMs { get; init; }
    public long GrantedMemoryKb { get; init; }
    public string Login { get; init; } = "";
    public string Host { get; init; } = "";
    public string Program { get; init; } = "";
    public string Statement { get; init; } = "";
    public bool IsBlocked => BlockingSessionId > 0;
    public string BlockedByText => BlockingSessionId > 0 ? BlockingSessionId.ToString() : "";
    /// <summary>Red tint on rows whose session is blocked by another session.</summary>
    public IBrush RowBackground => IsBlocked
        ? new SolidColorBrush(Color.Parse("#59803030"))
        : Brushes.Transparent;
}

public sealed class HealthWaitRow
{
    public string WaitType { get; init; } = "";
    public double WaitSeconds { get; init; }
    public double SignalSeconds { get; init; }
    public long WaitingTasks { get; init; }
    /// <summary>Share of the total wait time of the returned (top N) rows.</summary>
    public double SharePercent { get; set; }
}

public sealed class HealthExpensiveQueryRow
{
    public string Statement { get; init; } = "";
    public string Database { get; init; } = "";
    public long ExecutionCount { get; init; }
    public double TotalCpuMs { get; init; }
    public double AvgCpuMs { get; init; }
    public double TotalElapsedMs { get; init; }
    public double AvgElapsedMs { get; init; }
    public long TotalReads { get; init; }
    public double AvgReads { get; init; }
    public DateTime? LastExecution { get; init; }
}

public sealed class HealthFileIoRow
{
    public string Database { get; init; } = "";
    public string File { get; init; } = "";
    public string TypeDesc { get; init; } = "";
    public double SizeMb { get; init; }
    public long NumOfReads { get; init; }
    public long NumOfWrites { get; init; }
    public double ReadStallSec { get; init; }
    public double WriteStallSec { get; init; }
    public double AvgStallMs { get; init; }
}

/// <summary>One process (participant) inside a deadlock graph.</summary>
public sealed class HealthDeadlockProcess
{
    public string ProcessId { get; init; } = "";
    public string Spid { get; init; } = "";
    public string Login { get; init; } = "";
    public string Host { get; init; } = "";
    public string App { get; init; } = "";
    public string WaitResource { get; init; } = "";
    public string WaitMs { get; init; } = "";
    public string LockMode { get; init; } = "";
    public string Statement { get; init; } = "";
    public string InputBuf { get; init; } = "";
    public bool IsVictim { get; set; }
    public string VictimText => IsVictim ? "VICTIM" : "";
    /// <summary>Red tint on the killed-off process.</summary>
    public IBrush RowBackground => IsVictim
        ? new SolidColorBrush(Color.Parse("#59803030"))
        : Brushes.Transparent;
}

/// <summary>A parsed deadlock graph from the system_health extended-events ring buffer.</summary>
public sealed class HealthDeadlockReport
{
    public DateTime TimeUtc { get; init; }
    public string VictimSpid { get; init; } = "";
    public List<HealthDeadlockProcess> Processes { get; init; } = [];
    public string RawXml { get; init; } = "";
    public string ListTitle => TimeUtc.ToLocalTime().ToString("MM-dd  HH:mm:ss");
    public string ListSubtitle =>
        (VictimSpid.Length > 0 ? $"victim SPID {VictimSpid}" : "no victim recorded") +
        $" • {Processes.Count} process(es)";
    public string VictimInputBuf =>
        Processes.FirstOrDefault(p => p.IsVictim)?.InputBuf ?? Processes.FirstOrDefault()?.InputBuf ?? "";
}

/// <summary>Everything one refresh pass produced. Slow sections (waits, expensive
/// queries, deadlocks, file I/O) may be null when the refresh was a fast pass.</summary>
public sealed class DbHealthSnapshot
{
    public DateTime CollectedUtc { get; init; }
    public HealthServerInfo? ServerInfo { get; init; }
    public double SqlCpuPercent { get; init; }
    public double SystemIdlePercent { get; init; }
    public double OtherProcessPercent { get; init; }
    public List<CpuSample> CpuHistory { get; init; } = [];
    public long PhysicalMemoryKb { get; init; }
    public long CommittedKb { get; init; }
    public long CommittedTargetKb { get; init; }
    public long ProcessPhysicalKb { get; init; }
    public long WaitingTasks { get; init; }
    public long RunnableTasks { get; init; }
    public long PendingDiskIo { get; init; }
    public HealthCounterSample Counters { get; init; } = new() { Values = [] };
    public List<HealthProcessRow> Processes { get; init; } = [];
    public List<HealthWaitRow>? Waits { get; init; }
    public List<HealthExpensiveQueryRow>? ExpensiveQueries { get; init; }
    public List<HealthDeadlockReport>? Deadlocks { get; init; }
    public List<HealthFileIoRow>? FileIo { get; init; }
    public List<string>? Databases { get; init; }
    /// <summary>Sections that could not be read (permissions / Azure limits).</summary>
    public List<string> Warnings { get; init; } = [];
}
