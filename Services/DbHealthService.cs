using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Reads live server health data from SQL Server DMVs — the same sources SSMS
/// Activity Monitor uses — plus deadlock graphs from system_health. One
/// connection is reused between refreshes. Per-section failures are collected
/// into <see cref="DbHealthSnapshot.Warnings"/> instead of failing the pass.
/// </summary>
public sealed class DbHealthService : IDisposable
{
    private SqlConnection? _conn;
    private string? _connString;

    public async Task<DbHealthSnapshot> CollectAsync(ConnectionInfo info, bool includeSlow, CancellationToken ct,
        Action<string>? onSection = null)
    {
        var warnings = new List<string>();
        var conn = await GetOpenAsync(info, ct).ConfigureAwait(false);

        var serverInfo = default(HealthServerInfo);
        List<CpuSample> cpuHistory = [];
        long physicalMemoryKb = 0, committedKb = 0, committedTargetKb = 0, processPhysicalKb = 0;
        long waitingTasks = 0, runnableTasks = 0, pendingDiskIo = 0;
        HealthCounterSample counters = new() { Utc = DateTime.UtcNow, Values = [] };
        var processes = new List<HealthProcessRow>();
        List<string>? databases = null;
        List<HealthWaitRow>? waits = null;
        List<HealthExpensiveQueryRow>? expensive = null;
        List<HealthDeadlockReport>? deadlocks = null;
        List<HealthFileIoRow>? fileIo = null;
        List<HealthMissingIndexRow>? missing = null;

        Run("server info", onSection, () => serverInfo = ReadServerInfo(conn, ct), warnings, ct);
        Run("cpu ring buffer", onSection, () => cpuHistory = ReadCpuHistory(conn, ct), warnings, ct);
        Run("memory", onSection, () => (physicalMemoryKb, committedKb, committedTargetKb, processPhysicalKb) = ReadMemory(conn, ct), warnings, ct);
        Run("schedulers", onSection, () => (waitingTasks, runnableTasks, pendingDiskIo) = ReadSchedulers(conn, ct), warnings, ct);
        Run("perf counters", onSection, () => counters = ReadCounters(conn, ct), warnings, ct);
        Run("processes", onSection, () => processes = ReadProcesses(conn, ct), warnings, ct);
        if (includeSlow)
            Run("database list", onSection, () => databases = ReadDatabases(conn, ct), warnings, ct);

        if (includeSlow)
        {
            Run("wait stats", onSection, () => waits = ReadWaits(conn, ct), warnings, ct);
            Run("expensive queries", onSection, () => expensive = ReadExpensiveQueries(conn, ct), warnings, ct);
            Run("deadlocks", onSection, () => deadlocks = ReadDeadlocks(conn, ct), warnings, ct);
            Run("file I/O", onSection, () => fileIo = ReadFileIo(conn, ct), warnings, ct);
            Run("missing indexes", onSection, () => missing = ReadMissingIndexes(conn, ct), warnings, ct);
        }

        ct.ThrowIfCancellationRequested();
        var lastCpu = cpuHistory.Count > 0 ? cpuHistory[^1] : null;
        return new DbHealthSnapshot
        {
            CollectedUtc = DateTime.UtcNow,
            ServerInfo = serverInfo,
            SqlCpuPercent = lastCpu?.SqlPercent ?? 0,
            SystemIdlePercent = lastCpu?.IdlePercent ?? 0,
            OtherProcessPercent = lastCpu?.OtherPercent ?? 0,
            CpuHistory = cpuHistory,
            PhysicalMemoryKb = physicalMemoryKb,
            CommittedKb = committedKb,
            CommittedTargetKb = committedTargetKb,
            ProcessPhysicalKb = processPhysicalKb,
            WaitingTasks = waitingTasks,
            RunnableTasks = runnableTasks,
            PendingDiskIo = pendingDiskIo,
            Counters = counters,
            Processes = processes,
            Waits = waits,
            ExpensiveQueries = expensive,
            Deadlocks = deadlocks,
            FileIo = fileIo,
            MissingIndexes = missing,
            Databases = databases,
            Warnings = warnings
        };
    }

    private void Run(string section, Action<string>? onSection, Action read, List<string> warnings,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            onSection?.Invoke($"{section} starting");
            read();
            onSection?.Invoke($"{section} ok in {sw.ElapsedMilliseconds} ms");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Catch everything a DMV/XML read can throw (SqlException, XmlException,
            // InvalidCastException, overflow) so one bad section cannot take down
            // the monitor the way SSMS Activity Monitor does on truncated XML.
            warnings.Add($"{section}: {ex.Message}");
            onSection?.Invoke($"{section} FAILED in {sw.ElapsedMilliseconds} ms: {ex.Message}");
            if (ex is SqlException or System.IO.IOException)
                DisposeConnection();
            else
                ResetConnectionIfBroken();
        }
    }

    private async Task<SqlConnection> GetOpenAsync(ConnectionInfo info, CancellationToken ct)
    {
        var cs = info.ConnectionString;
        if (_conn is { State: ConnectionState.Open } &&
            string.Equals(_connString, cs, StringComparison.Ordinal))
            return _conn;

        DisposeConnection();
        _conn = new SqlConnection(cs);
        _connString = cs;
        await _conn.OpenAsync(ct).ConfigureAwait(false);
        return _conn;
    }

    private void ResetConnectionIfBroken()
    {
        if (_conn is null) return;
        if (_conn.State is ConnectionState.Broken or ConnectionState.Closed)
            DisposeConnection();
    }

    private void DisposeConnection()
    {
        try { _conn?.Dispose(); }
        catch { /* ignore dispose of a doomed connection */ }
        _conn = null;
        _connString = null;
    }

    private static SqlCommand Cmd(SqlConnection conn, string sql, CancellationToken ct, int timeoutSeconds)
    {
        var cmd = new SqlCommand(sql, conn) { CommandTimeout = timeoutSeconds };
        if (ct.CanBeCanceled)
        {
            var reg = ct.Register(() =>
            {
                try { cmd.Cancel(); }
                catch { /* already completed */ }
            });
            cmd.Disposed += (_, _) =>
            {
                try { reg.Dispose(); }
                catch { /* ignore */ }
            };
        }
        return cmd;
    }

    private static List<T> ReadList<T>(SqlCommand cmd, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            list.Add(map(reader));
        }
        return list;
    }

    private static HealthServerInfo ReadServerInfo(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = Cmd(conn, """
            SELECT @@SERVERNAME AS server_name,
                   @@VERSION AS version,
                   CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(64)) AS level,
                   osi.sqlserver_start_time, osi.cpu_count, osi.hyperthread_ratio,
                   osi.scheduler_count, osi.physical_memory_kb
            FROM sys.dm_os_sys_info AS osi
            """, ct, 15);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return new HealthServerInfo { ServerName = "?" };
        var version = GetString(reader, "version");
        var lines = version.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 2)
            version = $"{lines[0]} • {lines[^1]}";
        return new HealthServerInfo
        {
            ServerName = GetString(reader, "server_name"),
            ProductVersion = version,
            ProductLevel = GetString(reader, "level"),
            SqlServerStartTimeUtc = GetDateTime(reader, "sqlserver_start_time")?.ToUniversalTime(),
            CpuCount = GetInt32(reader, "cpu_count"),
            HyperthreadRatio = GetInt32(reader, "hyperthread_ratio"),
            SchedulerCount = GetInt32(reader, "scheduler_count"),
            PhysicalMemoryKb = GetInt64(reader, "physical_memory_kb")
        };
    }

    /// <summary>Scheduler-monitor ring buffer (SSMS "Processor Time %").
    /// DATEADD(ms, bigint) overflows INT on long-uptime servers — convert to
    /// seconds first. TRY_CONVERT skips corrupt ring-buffer payloads.</summary>
    private static List<CpuSample> ReadCpuHistory(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = Cmd(conn, """
            SET QUOTED_IDENTIFIER ON;
            WITH rb AS (
                SELECT [timestamp], TRY_CONVERT(xml, record) AS rec
                FROM sys.dm_os_ring_buffers
                WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR'
                  AND record LIKE N'%SystemHealth%'
            ),
            tops AS (
                SELECT TOP (240) [timestamp], rec
                FROM rb
                WHERE rec IS NOT NULL
                ORDER BY [timestamp] DESC
            )
            SELECT DATEADD(second, -CAST((osi.ms_ticks - tops.[timestamp]) / 1000 AS int), SYSDATETIME()) AS event_time,
                   tops.rec.value('(./Record/SchedulerMonitorEvent/SystemHealth/SQLProcessUtilization)[1]', 'int') AS sql_cpu,
                   tops.rec.value('(./Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int') AS system_idle
            FROM tops CROSS JOIN sys.dm_os_sys_info AS osi
            ORDER BY tops.[timestamp] ASC
            """, ct, 15);
        return ReadList(cmd, r =>
        {
            var sql = GetInt32(r, "sql_cpu");
            var idle = GetInt32(r, "system_idle");
            return new CpuSample
            {
                Time = GetDateTime(r, "event_time") ?? DateTime.MinValue,
                SqlPercent = sql,
                IdlePercent = idle,
                OtherPercent = Math.Clamp(100 - sql - idle, 0, 100)
            };
        }, ct);
    }

    private static (long Physical, long Committed, long CommittedTarget, long ProcessPhysical) ReadMemory(
        SqlConnection conn, CancellationToken ct)
    {
        using var cmd = Cmd(conn, """
            SELECT osi.physical_memory_kb, osi.committed_kb, osi.committed_target_kb,
                   ISNULL(opm.physical_memory_in_use_kb, 0) AS process_kb
            FROM sys.dm_os_sys_info AS osi
            LEFT JOIN sys.dm_os_process_memory AS opm ON 1 = 1
            """, ct, 15);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return (0, 0, 0, 0);
        return (
            GetInt64(reader, "physical_memory_kb"),
            GetInt64(reader, "committed_kb"),
            GetInt64(reader, "committed_target_kb"),
            GetInt64(reader, "process_kb"));
    }

    private static (long Waiting, long Runnable, long PendingIo) ReadSchedulers(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = Cmd(conn, """
            SELECT ISNULL(SUM(CAST(work_queue_count AS bigint)), 0) AS waiting,
                   ISNULL(SUM(CAST(runnable_tasks_count AS bigint)), 0) AS runnable,
                   ISNULL(SUM(CAST(pending_disk_io_count AS bigint)), 0) AS pending_io
            FROM sys.dm_os_schedulers
            WHERE status = N'VISIBLE ONLINE'
            """, ct, 15);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return (0, 0, 0);
        return (
            GetInt64(reader, "waiting"),
            GetInt64(reader, "runnable"),
            GetInt64(reader, "pending_io"));
    }

    /// <summary>Counter names we surface. Per-second counters are cumulative
    /// (cntr_type 272696576) — the VM divides by elapsed time between samples.</summary>
    internal static readonly string[] TrackedCounters =
    [
        "Batch Requests/sec", "SQL Re-Compilations/sec", "Transactions/sec",
        "Page Splits/sec", "Full Scans/sec", "Forwarded Records/sec",
        "Page life expectancy", "Buffer cache hit ratio", "Buffer cache hit ratio base",
        "Memory Grants Pending", "Memory Grants Outstanding",
        "Total Server Memory (KB)", "Target Server Memory (KB)",
        "User Connections", "Processes blocked",
        "Number of Deadlocks/sec", "Lock Waits/sec", "Lock Timeouts/sec",
        "Checkpoint pages/sec", "Errors/sec"
    ];

    private static HealthCounterSample ReadCounters(SqlConnection conn, CancellationToken ct)
    {
        var names = string.Join(",", TrackedCounters.Select(n => "N'" + n.Replace("'", "''") + "'"));
        using var cmd = Cmd(conn,
            $"""
             SELECT RTRIM(counter_name) AS counter_name,
                    RTRIM(instance_name) AS instance_name,
                    cntr_value
             FROM sys.dm_os_performance_counters
             WHERE RTRIM(counter_name) IN ({names})
               AND (RTRIM(instance_name) IN (N'', N'_Total') OR RTRIM(counter_name) = N'Page life expectancy')
             """, ct, 15);
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                var name = GetString(reader, "counter_name").Trim();
                if (name.Length == 0) continue;
                values[name] = GetInt64(reader, "cntr_value");
            }
        }
        return new HealthCounterSample { Utc = DateTime.UtcNow, Values = values };
    }

    /// <summary>Safe SUBSTRING of the current statement. Negative length
    /// (statement_end_offset &lt; start) is a common SSMS / DMV crash.</summary>
    private const string StatementTextSql = """
        CASE
            WHEN t.text IS NULL THEN N''
            ELSE SUBSTRING(
                t.text,
                (CASE WHEN ISNULL(r.statement_start_offset, 0) < 0 THEN 0
                      ELSE ISNULL(r.statement_start_offset, 0) END / 2) + 1,
                CASE
                    WHEN r.statement_end_offset IS NULL
                      OR r.statement_end_offset < ISNULL(r.statement_start_offset, 0) THEN 4000
                    WHEN r.statement_end_offset = -1 THEN
                        CASE WHEN (DATALENGTH(t.text) / 2) - (ISNULL(r.statement_start_offset, 0) / 2) < 0 THEN 0
                             ELSE (DATALENGTH(t.text) / 2) - (ISNULL(r.statement_start_offset, 0) / 2) + 1 END
                    ELSE ((r.statement_end_offset - ISNULL(r.statement_start_offset, 0)) / 2) + 1
                END)
        END
        """;

    private static List<HealthProcessRow> ReadProcesses(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = Cmd(conn, $"""
            SELECT s.session_id,
                   ISNULL(r.blocking_session_id, 0) AS blocking_session_id,
                   ISNULL(DB_NAME(COALESCE(r.database_id, s.database_id)), N'') AS db_name,
                   ISNULL(r.status, s.status) AS status,
                   ISNULL(r.command, N'') AS command,
                   ISNULL(r.wait_type, N'') AS wait_type,
                   ISNULL(r.wait_time, 0) AS wait_ms,
                   ISNULL(r.cpu_time, s.cpu_time) AS cpu_time,
                   ISNULL(r.logical_reads, s.logical_reads) AS logical_reads,
                   ISNULL(r.reads, s.reads) AS reads,
                   ISNULL(r.writes, s.writes) AS writes,
                   ISNULL(r.total_elapsed_time, s.total_elapsed_time) AS total_elapsed_time,
                   CAST(ISNULL(r.granted_query_memory, 0) AS bigint) * 8 AS granted_kb,
                   s.login_name,
                   ISNULL(s.host_name, N'') AS host_name,
                   ISNULL(s.program_name, N'') AS program_name,
                   {StatementTextSql} AS stmt_text
            FROM sys.dm_exec_sessions AS s
            LEFT JOIN sys.dm_exec_requests AS r
                   ON r.session_id = s.session_id AND r.session_id <> @@SPID
            OUTER APPLY sys.dm_exec_sql_text(COALESCE(r.sql_handle,
                (SELECT TOP (1) c.most_recent_sql_handle
                 FROM sys.dm_exec_connections AS c
                 WHERE c.session_id = s.session_id))) AS t
            WHERE s.is_user_process = 1 AND s.session_id <> @@SPID
            ORDER BY CASE WHEN r.session_id IS NULL THEN 1 ELSE 0 END, s.session_id
            """, ct, 15);
        return ReadList(cmd, r => new HealthProcessRow
        {
            SessionId = GetInt32(r, "session_id"),
            BlockingSessionId = GetInt32(r, "blocking_session_id"),
            Database = GetString(r, "db_name"),
            Status = GetString(r, "status"),
            Command = GetString(r, "command"),
            WaitType = GetString(r, "wait_type"),
            WaitMs = GetInt64(r, "wait_ms"),
            CpuMs = GetInt64(r, "cpu_time"),
            LogicalReads = GetInt64(r, "logical_reads"),
            Reads = GetInt64(r, "reads"),
            Writes = GetInt64(r, "writes"),
            ElapsedMs = GetInt64(r, "total_elapsed_time"),
            GrantedMemoryKb = GetInt64(r, "granted_kb"),
            Login = GetString(r, "login_name"),
            Host = GetString(r, "host_name"),
            Program = GetString(r, "program_name"),
            Statement = Truncate(GetString(r, "stmt_text", trimEnd: false), 400)
        }, ct);
    }

    private static List<string> ReadDatabases(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = Cmd(conn,
            "SELECT name FROM sys.databases WHERE state_desc = N'ONLINE' ORDER BY name", ct, 15);
        return ReadList(cmd, r => GetString(r, "name"), ct);
    }

    private static readonly HashSet<string> BenignWaits = new(StringComparer.OrdinalIgnoreCase)
    {
        "SLEEP_TASK", "SLEEP_SYSTEMTASK", "SLEEP_BPOOL_FLUSH", "SLEEP_BUFFERPOOL_HELPLW",
        "SLEEP_DBSTARTUP", "SLEEP_DCOMSTARTUP", "SLEEP_MASTERDBREADY", "SLEEP_MASTERMDREADY",
        "SLEEP_MASTERUPGRADED", "SLEEP_MSDBSTARTUP", "SLEEP_TEMPDBSTARTUP",
        "WAITFOR", "BROKER_TO_FLUSH", "BROKER_TASK_STOP", "BROKER_EVENTHANDLER",
        "BROKER_RECEIVE_WAITFOR", "BROKER_TRANSMITTER", "CLR_AUTO_EVENT", "CLR_MANUAL_EVENT",
        "CHECKPOINT_QUEUE", "DIRTY_PAGE_POLL", "DISPATCHER_QUEUE_SEMAPHORE",
        "FT_IFTS_SCHEDULER_IDLE_WAIT", "KSOURCE_WAKEUP", "LAZYWRITER_SLEEP", "LOGMGR_QUEUE",
        "MEMORY_ALLOCATION_EXT", "ONDEMAND_TASK_QUEUE", "PVS_PREALLOCATE",
        "QDS_ASYNC_QUEUE", "QDS_CLEANUP_STALE_QUERIES_TASK_MAIN", "QDS_PERSIST_TASK_MAIN",
        "QDS_SHUTDOWN_QUEUE", "REQUEST_FOR_DEADLOCK_SEARCH", "SQLTRACE_BUFFER_FLUSH",
        "SQLTRACE_INCREMENTAL_FLUSH_SLEEP", "SQLTRACE_WAIT_ENTRIES", "SWITCH_INCOMPLETE",
        "XE_DISPATCHER_WAIT", "XE_TIMER_EVENT", "XE_LIVE_TARGET_TVF",
        "WAIT_XTP_RECOVERY", "VDI_CLIENT_OTHER", "RECOVER_WRITEFORWARD",
        "HADR_FILESTREAM_IOMGR_IOCOMPLETION"
    };

    private static List<HealthWaitRow> ReadWaits(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = Cmd(conn, """
            SELECT TOP (80) wait_type,
                   wait_time_ms / 1000.0 AS wait_s,
                   signal_wait_time_ms / 1000.0 AS signal_s,
                   waiting_tasks_count
            FROM sys.dm_os_wait_stats
            WHERE wait_time_ms > 0
            ORDER BY wait_time_ms DESC
            """, ct, 15);
        var rows = ReadList(cmd, r => new HealthWaitRow
        {
            WaitType = GetString(r, "wait_type"),
            WaitSeconds = GetDouble(r, "wait_s"),
            SignalSeconds = GetDouble(r, "signal_s"),
            WaitingTasks = GetInt64(r, "waiting_tasks_count")
        }, ct);
        return rows.Where(w => !BenignWaits.Contains(w.WaitType)
                               && !w.WaitType.StartsWith("SLEEP", StringComparison.OrdinalIgnoreCase)
                               && !w.WaitType.StartsWith("BROKER_", StringComparison.OrdinalIgnoreCase)
                               && !w.WaitType.StartsWith("XE_", StringComparison.OrdinalIgnoreCase))
                   .Take(30)
                   .ToList();
    }

    private static List<HealthExpensiveQueryRow> ReadExpensiveQueries(SqlConnection conn, CancellationToken ct)
    {
        // No dm_exec_query_plan — that XML is huge and a common timeout/OOM in SSMS.
        using var cmd = Cmd(conn, """
            SELECT TOP (50)
                   CASE
                       WHEN st.text IS NULL THEN N''
                       ELSE SUBSTRING(
                           st.text,
                           (CASE WHEN ISNULL(qs.statement_start_offset, 0) < 0 THEN 0
                                 ELSE ISNULL(qs.statement_start_offset, 0) END / 2) + 1,
                           CASE
                               WHEN qs.statement_end_offset IS NULL
                                 OR qs.statement_end_offset < ISNULL(qs.statement_start_offset, 0) THEN 4000
                               WHEN qs.statement_end_offset = -1 THEN
                                   CASE WHEN (DATALENGTH(st.text) / 2) - (ISNULL(qs.statement_start_offset, 0) / 2) < 0 THEN 0
                                        ELSE (DATALENGTH(st.text) / 2) - (ISNULL(qs.statement_start_offset, 0) / 2) + 1 END
                               ELSE ((qs.statement_end_offset - ISNULL(qs.statement_start_offset, 0)) / 2) + 1
                           END)
                   END AS stmt_text,
                   ISNULL(DB_NAME(st.dbid), N'') AS db_name,
                   qs.execution_count,
                   qs.total_worker_time / 1000.0 AS total_cpu_ms,
                   qs.total_elapsed_time / 1000.0 AS total_elapsed_ms,
                   qs.total_logical_reads,
                   qs.last_execution_time
            FROM sys.dm_exec_query_stats AS qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
            ORDER BY qs.total_worker_time DESC
            """, ct, 25);
        return ReadList(cmd, r =>
        {
            var execCount = GetInt64(r, "execution_count");
            var totalCpu = GetDouble(r, "total_cpu_ms");
            var totalElapsed = GetDouble(r, "total_elapsed_ms");
            var totalReads = GetInt64(r, "total_logical_reads");
            return new HealthExpensiveQueryRow
            {
                Statement = Truncate(GetString(r, "stmt_text", trimEnd: false), 2000),
                Database = GetString(r, "db_name"),
                ExecutionCount = execCount,
                TotalCpuMs = totalCpu,
                AvgCpuMs = execCount > 0 ? totalCpu / execCount : 0,
                TotalElapsedMs = totalElapsed,
                AvgElapsedMs = execCount > 0 ? totalElapsed / execCount : 0,
                TotalReads = totalReads,
                AvgReads = execCount > 0 ? (double)totalReads / execCount : 0,
                LastExecution = GetDateTime(r, "last_execution_time")
            };
        }, ct);
    }

    private static readonly Regex DeadlockEventRegex = new(
        """<event\s+name=["']xml_deadlock_report["'][\s\S]*?</event>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Deadlock graphs from the system_health ring buffer. If the
    /// buffer XML is truncated (the classic SSMS parse crash), extract
    /// individual event fragments instead of failing the whole refresh.</summary>
    private static List<HealthDeadlockReport> ReadDeadlocks(SqlConnection conn, CancellationToken ct)
    {
        string xml;
        using (var cmd = Cmd(conn, """
            SELECT CAST(xet.target_data AS nvarchar(max)) AS target_data
            FROM sys.dm_xe_session_targets AS xet
            JOIN sys.dm_xe_sessions AS xes ON xes.address = xet.event_session_address
            WHERE xes.name = N'system_health' AND xet.target_name = N'ring_buffer'
            """, ct, 20))
        {
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return [];
            xml = GetString(reader, "target_data");
        }
        if (xml.Length == 0) return [];

        var reports = new List<HealthDeadlockReport>();
        IEnumerable<string> fragments;
        try
        {
            var root = XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Root;
            fragments = root is null
                ? []
                : root.Descendants("event")
                    .Where(e => (string?)e.Attribute("name") == "xml_deadlock_report")
                    .TakeLast(30)
                    .Reverse()
                    .Select(e => e.ToString());
        }
        catch (XmlException)
        {
            fragments = DeadlockEventRegex.Matches(xml).Select(m => m.Value).TakeLast(30).Reverse();
        }

        foreach (var fragment in fragments)
        {
            ct.ThrowIfCancellationRequested();
            var report = ParseDeadlockEvent(fragment, ct);
            if (report != null)
                reports.Add(report);
        }
        return reports;
    }

    private static HealthDeadlockReport? ParseDeadlockEvent(string eventXml, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var ev = XElement.Parse(eventXml);
            if (!string.Equals((string?)ev.Attribute("name"), "xml_deadlock_report", StringComparison.OrdinalIgnoreCase)
                && ev.Name.LocalName != "deadlock")
            {
                var nested = ev.Descendants("event")
                    .FirstOrDefault(e => (string?)e.Attribute("name") == "xml_deadlock_report");
                if (nested != null) ev = nested;
            }

            var timeUtc = DateTime.MinValue;
            var ts = ev.Attribute("timestamp")?.Value;
            if (ts != null && DateTime.TryParse(ts, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                timeUtc = parsed;

            XElement? deadlock = ev.Name.LocalName == "deadlock"
                ? ev
                : ev.Element("data")?.Element("value")?.Element("deadlock");
            if (deadlock is null)
            {
                var value = ev.Element("data")?.Element("value");
                var raw = value?.Value;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try { deadlock = XElement.Parse(raw); }
                    catch (XmlException) { return null; }
                }
            }
            if (deadlock is null || deadlock.Name.LocalName != "deadlock")
                return null;

            var victimId = deadlock.Element("victim-list")?.Element("victimProcess")?.Attribute("id")?.Value ?? "";
            var processes = new List<HealthDeadlockProcess>();
            foreach (var p in deadlock.Element("process-list")?.Elements("process") ?? [])
            {
                var stmt = string.Join("\n",
                    p.Element("executionStack")?.Elements("frame").Select(f => f.Value.Trim()) ?? []);
                processes.Add(new HealthDeadlockProcess
                {
                    ProcessId = p.Attribute("id")?.Value ?? "",
                    Spid = p.Attribute("spid")?.Value ?? "",
                    Login = p.Attribute("loginname")?.Value ?? "",
                    Host = p.Attribute("hostname")?.Value ?? "",
                    App = p.Attribute("clientapp")?.Value ?? "",
                    WaitResource = p.Attribute("waitresource")?.Value ?? "",
                    WaitMs = p.Attribute("waittime")?.Value ?? "",
                    LockMode = p.Attribute("lockMode")?.Value ?? "",
                    Statement = Truncate(stmt, 2000),
                    InputBuf = Truncate(p.Element("inputbuf")?.Value.Trim() ?? "", 2000),
                    IsVictim = false
                });
            }
            foreach (var p in processes.Where(p => p.ProcessId == victimId))
                p.IsVictim = true;

            return new HealthDeadlockReport
            {
                TimeUtc = timeUtc,
                VictimSpid = processes.FirstOrDefault(p => p.IsVictim)?.Spid ?? "",
                Processes = processes,
                RawXml = deadlock.ToString()
            };
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static List<HealthFileIoRow> ReadFileIo(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = Cmd(conn, """
            SELECT TOP (60)
                   ISNULL(DB_NAME(vfs.database_id), N'') AS db_name,
                   mf.name AS file_name, mf.type_desc,
                   vfs.size_on_disk_bytes / 1048576.0 AS size_mb,
                   vfs.num_of_reads, vfs.num_of_writes,
                   vfs.io_stall_read_ms / 1000.0 AS read_stall_s,
                   vfs.io_stall_write_ms / 1000.0 AS write_stall_s,
                   CASE WHEN vfs.num_of_reads + vfs.num_of_writes = 0 THEN 0
                        ELSE CAST(vfs.io_stall_read_ms + vfs.io_stall_write_ms AS float)
                             / (vfs.num_of_reads + vfs.num_of_writes) END AS avg_stall_ms
            FROM sys.dm_io_virtual_file_stats(NULL, NULL) AS vfs
            JOIN sys.master_files AS mf
                 ON mf.database_id = vfs.database_id AND mf.file_id = vfs.file_id
            ORDER BY vfs.io_stall_read_ms + vfs.io_stall_write_ms DESC
            """, ct, 15);
        return ReadList(cmd, r => new HealthFileIoRow
        {
            Database = GetString(r, "db_name"),
            File = GetString(r, "file_name"),
            TypeDesc = GetString(r, "type_desc"),
            SizeMb = GetDouble(r, "size_mb"),
            NumOfReads = GetInt64(r, "num_of_reads"),
            NumOfWrites = GetInt64(r, "num_of_writes"),
            ReadStallSec = GetDouble(r, "read_stall_s"),
            WriteStallSec = GetDouble(r, "write_stall_s"),
            AvgStallMs = GetDouble(r, "avg_stall_ms")
        }, ct);
    }

    /// <summary>Missing-index recommendations, ranked like the SSMS
    /// "Missing Indexes Details" report: impact × (seeks + scans). The
    /// optimizer DMVs reset on server restart and only cover user databases.</summary>
    private static List<HealthMissingIndexRow> ReadMissingIndexes(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = Cmd(conn, """
            SELECT TOP (50)
                   ISNULL(DB_NAME(d.database_id), N'') AS db_name,
                   ISNULL(OBJECT_SCHEMA_NAME(d.object_id, d.database_id), N'') AS schema_name,
                   ISNULL(OBJECT_NAME(d.object_id, d.database_id), N'') AS table_name,
                   ISNULL(d.equality_columns, N'') AS equality_columns,
                   ISNULL(d.inequality_columns, N'') AS inequality_columns,
                   ISNULL(d.included_columns, N'') AS included_columns,
                   gs.unique_compiles,
                   gs.user_seeks,
                   gs.user_scans,
                   gs.avg_total_user_cost AS avg_user_cost,
                   gs.avg_user_impact,
                   gs.last_user_seek
            FROM sys.dm_db_missing_index_details AS d
            JOIN sys.dm_db_missing_index_groups AS g ON g.index_handle = d.index_handle
            JOIN sys.dm_db_missing_index_group_stats AS gs ON gs.group_handle = g.index_group_handle
            WHERE d.database_id > 4
              AND d.object_id IS NOT NULL
              AND OBJECT_NAME(d.object_id, d.database_id) IS NOT NULL
            ORDER BY gs.avg_user_impact * (gs.user_seeks + gs.user_scans) DESC
            """, ct, 20);
        return ReadList(cmd, r =>
        {
            var db = GetString(r, "db_name");
            var schema = GetString(r, "schema_name");
            var table = GetString(r, "table_name");
            // Some versions return bracket-wrapped names ([id]) — normalize.
            static string[] ColNames(string csv) =>
                csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Select(c => c.Trim('[', ']').Trim())
                   .Where(c => c.Length > 0)
                   .ToArray();
            var eqCols = ColNames(GetString(r, "equality_columns"));
            var ineqCols = ColNames(GetString(r, "inequality_columns"));
            var inclCols = ColNames(GetString(r, "included_columns"));
            return new HealthMissingIndexRow
            {
                Database = db,
                Schema = schema,
                Table = table,
                KeyColumns = string.Join(", ", eqCols.Concat(ineqCols)),
                IncludedColumns = string.Join(", ", inclCols),
                UniqueCompiles = GetInt64(r, "unique_compiles"),
                UserSeeks = GetInt64(r, "user_seeks"),
                UserScans = GetInt64(r, "user_scans"),
                AvgUserCost = GetDouble(r, "avg_user_cost"),
                AvgUserImpact = GetDouble(r, "avg_user_impact"),
                LastUserSeekUtc = GetDateTime(r, "last_user_seek")?.ToUniversalTime(),
                CreateScript = BuildCreateIndexScript(db, schema, table, eqCols, ineqCols, inclCols)
            };
        }, ct);
    }

    /// <summary>SSMS-details-report style CREATE NONCLUSTERED INDEX script.</summary>
    private static string BuildCreateIndexScript(string db, string schema, string table,
        string[] eqCols, string[] ineqCols, string[] inclCols)
    {
        static string Q(string col) => "[" + col.Replace("]", "]]") + "]";

        var keyCols = eqCols.Concat(ineqCols).Select(Q).ToArray();
        if (keyCols.Length == 0)
            return "";

        static string Safe(string s) => s.Replace("]", "]]");

        var name = $"IX_{Safe(table)}_{string.Join("_", eqCols.Concat(ineqCols))}";
        if (name.Length > 128) name = name[..128];

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"USE [{Safe(db)}];");
        sb.Append($"CREATE NONCLUSTERED INDEX [{name}] ON [{Safe(schema)}].[{Safe(table)}] ({string.Join(", ", keyCols)})");
        if (inclCols.Length > 0)
            sb.Append($"\n    INCLUDE ({string.Join(", ", inclCols.Select(Q))})");
        return sb.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + " ...";

    private static string GetString(SqlDataReader r, string col, bool trimEnd = true)
    {
        var v = r[col];
        if (v is null or DBNull) return "";
        var s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        return trimEnd ? s.TrimEnd() : s;
    }

    private static int GetInt32(SqlDataReader r, string col) =>
        (int)Math.Clamp(GetInt64(r, col), int.MinValue, int.MaxValue);

    private static long GetInt64(SqlDataReader r, string col)
    {
        var v = r[col];
        if (v is null or DBNull) return 0;
        return Convert.ToInt64(v, CultureInfo.InvariantCulture);
    }

    private static double GetDouble(SqlDataReader r, string col)
    {
        var v = r[col];
        if (v is null or DBNull) return 0;
        var d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
        return double.IsFinite(d) ? d : 0;
    }

    private static DateTime? GetDateTime(SqlDataReader r, string col)
    {
        var v = r[col];
        if (v is null or DBNull) return null;
        if (v is DateTime dt) return dt;
        if (v is DateTimeOffset dto) return dto.UtcDateTime;
        return Convert.ToDateTime(v, CultureInfo.InvariantCulture);
    }

    public void Dispose() => DisposeConnection();
}
