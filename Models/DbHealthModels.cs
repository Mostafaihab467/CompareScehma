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

// ═══════════════════ Query Store (one database at a time) ═══════════════════

/// <summary>What <c>sys.database_query_store_options</c> says about one database. Query
/// Store is off by default, so "no rows" is usually a configuration fact rather than an
/// empty result — the tab has to name it instead of showing a blank grid.</summary>
public sealed class QueryStoreState
{
    public string Database { get; init; } = "";
    public string DesiredState { get; init; } = "";
    public string ActualState { get; init; } = "";
    public string AdditionalInfo { get; init; } = "";
    public string QueryCaptureMode { get; init; } = "";
    public string WaitStatsCapture { get; init; } = "";
    public string SizeBasedCleanup { get; init; } = "";
    public long CurrentStorageMb { get; init; }
    public long MaxStorageMb { get; init; }
    public long FlushIntervalSeconds { get; init; }
    public long IntervalLengthMinutes { get; init; }
    public long StaleQueryThresholdDays { get; init; }
    public int MaxPlansPerQuery { get; init; }

    public bool IsOn => ActualState.Equals("READ_WRITE", StringComparison.OrdinalIgnoreCase)
                     || ActualState.Equals("READ_ONLY", StringComparison.OrdinalIgnoreCase);
    public bool IsReadOnly => ActualState.Equals("READ_ONLY", StringComparison.OrdinalIgnoreCase);

    /// <summary>The state plus the reason it is not collecting, in one line.</summary>
    public string Summary
    {
        get
        {
            if (string.IsNullOrEmpty(ActualState))
                return $"Query Store has no options for [{Database}] — the database is offline or the view is not readable.";
            if (!IsOn)
                return $"Query Store is {ActualState} on [{Database}] (desired: {DesiredState}). " +
                       "Nothing has been captured, so there is no history to list." +
                       (string.IsNullOrEmpty(AdditionalInfo) ? "" : $" The server said: {AdditionalInfo}");
            var s = $"Query Store is {ActualState} on [{Database}] — {CurrentStorageMb:N0} of {MaxStorageMb:N0} MB used, " +
                    $"one statistics interval per {IntervalLengthMinutes} min, flushed every {Math.Max(1, FlushIntervalSeconds / 60)} min.";
            if (!QueryCaptureMode.Equals("ALL", StringComparison.OrdinalIgnoreCase) && QueryCaptureMode.Length > 0)
                s += $" Capture mode: {QueryCaptureMode}.";
            if (IsReadOnly)
                s += " It is read-only, so a plan cannot be forced until it is writable again.";
            if (!string.IsNullOrEmpty(AdditionalInfo))
                s += $" Note: {AdditionalInfo}";
            return s;
        }
    }
}

/// <summary>One query whose average duration in the second half of the window is worse
/// than in the first half — the "Regressed Queries" report.</summary>
public sealed class QueryStoreRegressedRow
{
    public long QueryId { get; init; }
    public long PlanId { get; init; }
    public string ObjectName { get; init; } = "";
    public string QueryText { get; init; } = "";
    public double BeforeExecutions { get; init; }
    public double AfterExecutions { get; init; }
    public double BeforeAvgMs { get; init; }
    public double AfterAvgMs { get; init; }
    public double BeforeCpuMs { get; init; }
    public double AfterCpuMs { get; init; }
    public double BeforeReads { get; init; }
    public double AfterReads { get; init; }
    public DateTime? LastExecutedUtc { get; init; }
    public bool IsForced { get; init; }
    public int ForceFailureCount { get; init; }
    public string ForceFailureReason { get; init; } = "";

    public double ChangePercent => BeforeAvgMs <= 0 ? 0 : (AfterAvgMs - BeforeAvgMs) / BeforeAvgMs * 100;
    public string ChangeText => $"{ChangePercent:+0;-0;0.0}%";
    public string DurationText => $"{BeforeAvgMs:N1} → {AfterAvgMs:N1} ms";
    public string CpuText => $"{BeforeCpuMs:N1} → {AfterCpuMs:N1} ms";
    public string ReadsText => $"{BeforeReads:N0} → {AfterReads:N0}";
    public string OneLineQuery => OneLine(QueryText);

    internal static string OneLine(string text)
    {
        var flat = new string((text ?? "").Where(c => !char.IsControl(c) || c == '\n').ToArray())
            .Replace("\r", " ").Replace("\n", " ").Trim();
        while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
        return flat.Length <= 200 ? flat : flat[..200] + "…";
    }
}

/// <summary>One query/plan pair ranked by a resource the operator picked — the
/// "Top Resource Consuming Queries" report.</summary>
public sealed class QueryStoreTopRow
{
    public long QueryId { get; init; }
    public long PlanId { get; init; }
    public string ObjectName { get; init; } = "";
    public string QueryText { get; init; } = "";
    public double Executions { get; init; }
    public double AvgMs { get; init; }
    public double TotalMs { get; init; }
    public double AvgCpuMs { get; init; }
    public double AvgReads { get; init; }
    public double AvgWrites { get; init; }
    public double AvgRows { get; init; }
    public DateTime? LastExecutedUtc { get; init; }
    public bool IsForced { get; init; }
    public string ForceFailureReason { get; init; } = "";
    public string OneLineQuery => QueryStoreRegressedRow.OneLine(QueryText);
}

/// <summary>Every plan Query Store holds for one query, which is what a plan choice is
/// made from: cost, whether it is already forced, and why forcing failed before.</summary>
public sealed class QueryStorePlanRow
{
    public long PlanId { get; init; }
    public DateTime? CreatedUtc { get; init; }
    public DateTime? LastExecutedUtc { get; init; }
    public double Executions { get; init; }
    public double AvgMs { get; init; }
    public int Compiles { get; init; }
    public bool IsForced { get; init; }
    public int ForceFailureCount { get; init; }
    public string ForceFailureReason { get; init; } = "";
    public string PlanXml { get; init; } = "";

    public string StatusText => IsForced
        ? (ForceFailureCount > 0 ? $"forced — {ForceFailureCount} failure(s): {ForceFailureReason}" : "forced")
        : ForceFailureCount > 0 ? $"not forced ({ForceFailureCount} past failure(s))" : "not forced";
}
