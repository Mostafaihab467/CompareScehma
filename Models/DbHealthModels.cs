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

/// <summary>One missing-index recommendation from the optimizer DMVs — the same
/// numbers SSMS shows in the "Missing Indexes Details" standard report.
/// <see cref="EffectiveScore"/> (impact × uses) is how the list is ranked.</summary>
public sealed class HealthMissingIndexRow
{
    public string Database { get; init; } = "";
    public string Schema { get; init; } = "";
    public string Table { get; init; } = "";
    /// <summary>Equality + inequality key columns, comma-separated.</summary>
    public string KeyColumns { get; init; } = "";
    /// <summary>INCLUDE columns, comma-separated ("" when none).</summary>
    public string IncludedColumns { get; init; } = "";
    public long UniqueCompiles { get; init; }
    public long UserSeeks { get; init; }
    public long UserScans { get; init; }
    public double AvgUserCost { get; init; }
    public double AvgUserImpact { get; init; }
    public DateTime? LastUserSeekUtc { get; init; }
    public string CreateScript { get; init; } = "";

    /// <summary>SSMS-style effectiveness: avg_user_impact × (user_seeks + user_scans).</summary>
    public double EffectiveScore => AvgUserImpact * (UserSeeks + UserScans);
    public string ImpactText => $"{AvgUserImpact:0.#}%";
    public string EffectiveText => EffectiveScore.ToString("N0");
    public string ObjectText => $"{Database}.{Schema}.{Table}";
    public IBrush ImpactBrush => AvgUserImpact >= 80
        ? new SolidColorBrush(Color.Parse("#EF6B73"))
        : AvgUserImpact >= 50
            ? new SolidColorBrush(Color.Parse("#E5B567"))
            : Brushes.Transparent;
}

/// <summary>One file of the database (used by the DB Manager properties dialog).</summary>
public sealed class DbFileInfo
{
    public string Name { get; init; } = "";
    public string TypeDesc { get; init; } = "";
    public double SizeMb { get; init; }
    public double UsedMb { get; init; }
    public string PhysicalName { get; init; } = "";
    public string GrowthText { get; init; } = "";
    public string StateDesc { get; init; } = "";
    public string FreeText => $"{Math.Max(0, SizeMb - UsedMb):N1} MB";
}

/// <summary>Snapshot of one database's core properties (SSMS Properties dialog).</summary>
public sealed class DatabaseProperties
{
    public string Name { get; init; } = "";
    public DateTime? CreatedUtc { get; init; }
    public string CompatibilityLevel { get; init; } = "";
    public string Collation { get; init; } = "";
    public string RecoveryModel { get; init; } = "";
    public string State { get; init; } = "";
    public string UserAccess { get; init; } = "";
    public string LogReuseWait { get; init; } = "";
    public bool IsRcsi { get; init; }
    public List<DbFileInfo> Files { get; init; } = [];
    public double LogTotalMb { get; init; }
    public double LogUsedMb { get; init; }
    public double DataSizeMb => Files.Where(f => f.TypeDesc == "ROWS").Sum(f => f.SizeMb);
    public double DataUsedMb => Files.Where(f => f.TypeDesc == "ROWS").Sum(f => f.UsedMb);
    public string CreatedText => CreatedUtc?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string DataSizeText => $"{DataSizeMb:N1} MB";
    public string DataFreeText => $"{Math.Max(0, DataSizeMb - DataUsedMb):N1} MB";
    public string LogText => $"{LogUsedMb:N1} / {LogTotalMb:N1} MB";
    public string RcsiText => IsRcsi ? "ON" : "OFF";
}

/// <summary>One database user or role (read-only, Security folder in DB Manager).</summary>
public sealed class DbPrincipalRow
{
    public string Name { get; init; } = "";
    public string TypeDesc { get; init; } = "";
    public string DefaultSchema { get; init; } = "";
    public DateTime? CreatedUtc { get; init; }
    public bool IsFixedRole { get; init; }
    public string Owner { get; init; } = "";
    public string Detail =>
        IsFixedRole ? "fixed role"
        : TypeDesc.Contains("ROLE") ? $"role, owned by {Owner}"
        : $"default schema: {DefaultSchema}";
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
    public List<HealthMissingIndexRow>? MissingIndexes { get; init; }
    public List<string>? Databases { get; init; }
    /// <summary>Sections that could not be read (permissions / Azure limits).</summary>
    public List<string> Warnings { get; init; } = [];
}
