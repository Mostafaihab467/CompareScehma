using System.Data;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Reads live server health data from SQL Server DMVs — the same sources SSMS
/// Activity Monitor uses — plus deadlock graphs from the system_health extended
/// events ring buffer. All queries run on the thread pool; one connection is
/// kept open and reused between refreshes. Per-section failures are collected
/// into <see cref="DbHealthSnapshot.Warnings"/> instead of failing the pass.
/// </summary>
public sealed class DbHealthService : IDisposable
{
    private SqlConnection? _conn;

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

        // --- fast sections (every tick) ---
        Run("server info", onSection, () => serverInfo = ReadServerInfo(conn, ct), warnings);
        Run("cpu ring buffer", onSection, () => cpuHistory = ReadCpuHistory(conn, ct), warnings);
        Run("memory", onSection, () => (physicalMemoryKb, committedKb, committedTargetKb, processPhysicalKb) = ReadMemory(conn, ct), warnings);
        Run("schedulers", onSection, () => (waitingTasks, runnableTasks, pendingDiskIo) = ReadSchedulers(conn, ct), warnings);
        Run("perf counters", onSection, () => counters = ReadCounters(conn, ct), warnings);
        Run("processes", onSection, () => processes = ReadProcesses(conn, ct), warnings);
        if (includeSlow)
            Run("database list", onSection, () => databases = ReadDatabases(conn, ct), warnings);

        // --- slow sections (periodic / on demand) ---
        if (includeSlow)
        {
            Run("wait stats", onSection, () => waits = ReadWaits(conn, ct), warnings);
            Run("expensive queries", onSection, () => expensive = ReadExpensiveQueries(conn, ct), warnings);
            Run("deadlocks", onSection, () => deadlocks = ReadDeadlocks(conn, ct), warnings);
            Run("file I/O", onSection, () => fileIo = ReadFileIo(conn, ct), warnings);
        }

        ct.ThrowIfCancellationRequested();
        return new DbHealthSnapshot
        {
            CollectedUtc = DateTime.UtcNow,
            ServerInfo = serverInfo,
            SqlCpuPercent = cpuHistory.Count > 0 ? cpuHistory[^1].SqlPercent : 0,
            SystemIdlePercent = cpuHistory.Count > 0 ? cpuHistory[^1].OtherPercent : 0,
            OtherProcessPercent = 0,
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
            Databases = databases,
            Warnings = warnings
        };

        static void Run(string section, Action<string>? onSection, Action read, List<string> warnings)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                onSection?.Invoke($"{section} starting");
                read();
                onSection?.Invoke($"{section} ok in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                warnings.Add($"{section}: {ex.Message}");
                onSection?.Invoke($"{section} FAILED in {sw.ElapsedMilliseconds} ms: {ex.Message}");
            }
        }
    }

    private async Task<SqlConnection> GetOpenAsync(ConnectionInfo info, CancellationToken ct)
    {
        if (_conn is { State: ConnectionState.Open })
            return _conn;
        _conn?.Dispose();
        _conn = new SqlConnection(info.ConnectionString);
        await _conn.OpenAsync(ct).ConfigureAwait(false);
        return _conn;
    }

    private static List<T> ReadList<T>(SqlCommand cmd, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
            list.Add(map(reader));
        return list;
    }

    private static HealthServerInfo ReadServerInfo(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = new SqlCommand("""
            SELECT @@SERVERNAME AS server_name,
                   @@VERSION AS version,
                   CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(64)) AS level,
                   osi.sqlserver_start_time, osi.cpu_count, osi.hyperthread_ratio,
                   osi.scheduler_count, osi.physical_memory_kb
            FROM sys.dm_os_sys_info AS osi
            """, conn) { CommandTimeout = 15 };
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return new HealthServerInfo { ServerName = "?" };
        var version = reader["version"] as string ?? "";
        // @@VERSION spans several lines (build, date, copyright, edition) — keep
        // the product head and the edition on one line for the info strip.
        var lines = version.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 2)
            version = $"{lines[0]} • {lines[^1]}";
        return new HealthServerInfo
        {
            ServerName = reader["server_name"] as string ?? "",
            ProductVersion = version,
            ProductLevel = reader["level"] as string ?? "",
            SqlServerStartTimeUtc = reader["sqlserver_start_time"] is DateTime st ? st.ToUniversalTime() : null,
            CpuCount = reader["cpu_count"] as int? ?? 0,
            HyperthreadRatio = reader["hyperthread_ratio"] as int? ?? 0,
            SchedulerCount = reader["scheduler_count"] as int? ?? 0,
            PhysicalMemoryKb = reader["physical_memory_kb"] as long? ?? 0
        };
    }

    /// <summary>Returns the scheduler-monitor ring buffer history (the source SSMS
    /// graphs "Processor Time %"). Each record is ~1 per minute, up to ~5 hours back.</summary>
    private static List<CpuSample> ReadCpuHistory(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = new SqlCommand("""
            SET QUOTED_IDENTIFIER ON;
            WITH rb AS (
                SELECT [timestamp],
                       CONVERT(xml, record) AS rec
                FROM sys.dm_os_ring_buffers
                WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR'
                  AND record LIKE '%SystemHealth%'
            ),
            tops AS (
                SELECT TOP (240) [timestamp], rec
                FROM rb
                ORDER BY [timestamp] DESC
            )
            SELECT DATEADD(ms, tops.[timestamp] - osi.ms_ticks, SYSDATETIME()) AS event_time,
                   tops.rec.value('(./Record/SchedulerMonitorEvent/SystemHealth/SQLProcessUtilization)[1]', 'int') AS sql_cpu,
                   tops.rec.value('(./Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int') AS system_idle
            FROM tops CROSS JOIN sys.dm_os_sys_info AS osi
            ORDER BY tops.[timestamp] ASC
            """, conn) { CommandTimeout = 15 };
        return ReadList(cmd, r => new CpuSample
        {
            Time = r["event_time"] is DateTime t ? t : DateTime.MinValue,
            SqlPercent = r["sql_cpu"] as int? ?? 0,
            OtherPercent = Math.Clamp(100 - (r["sql_cpu"] as int? ?? 0) - (r["system_idle"] as int? ?? 0), 0, 100)
        }, ct);
    }

    private static (long Physical, long Committed, long CommittedTarget, long ProcessPhysical) ReadMemory(
        SqlConnection conn, CancellationToken ct)
    {
        using var cmd = new SqlCommand("""
            SELECT osi.physical_memory_kb, osi.committed_kb, osi.committed_target_kb,
                   ISNULL(opm.physical_memory_in_use_kb, 0) AS process_kb
            FROM sys.dm_os_sys_info AS osi
            LEFT JOIN sys.dm_os_process_memory AS opm ON 1 = 1
            """, conn) { CommandTimeout = 15 };
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return (0, 0, 0, 0);
        return (
            reader["physical_memory_kb"] as long? ?? 0,
            reader["committed_kb"] as long? ?? 0,
            reader["committed_target_kb"] as long? ?? 0,
            reader["process_kb"] as long? ?? 0);
    }

    private static (long Waiting, long Runnable, long PendingIo) ReadSchedulers(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = new SqlCommand("""
            SELECT ISNULL(SUM(work_queue_count), 0) AS waiting,
                   ISNULL(SUM(runnable_tasks_count), 0) AS runnable,
                   ISNULL(SUM(pending_disk_io_count), 0) AS pending_io
            FROM sys.dm_os_schedulers
            WHERE status = 'VISIBLE ONLINE'
            """, conn) { CommandTimeout = 15 };
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return (0, 0, 0);
        return (
            reader["waiting"] as long? ?? 0,
            reader["runnable"] as long? ?? 0,
            reader["pending_io"] as long? ?? 0);
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
        using var cmd = new SqlCommand(
            $"SELECT counter_name, instance_name, cntr_value FROM sys.dm_os_performance_counters " +
            $"WHERE counter_name IN ({names})", conn) { CommandTimeout = 15 };
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var name = reader["counter_name"] as string ?? "";
                var instance = reader["instance_name"] as string ?? "";
                var value = reader["cntr_value"] as long? ?? 0;
                // Per-database counters (e.g. Transactions/sec) are summed across
                // instances; lock/error counters use the _Total instance.
                var key = instance is "_Total" or "" ? name : $"{name}|{instance}";
                if (TrackedCounters.Contains(name) && (instance == "_Total" || instance == ""))
                {
                    values[name] = values.TryGetValue(name, out var cur) ? cur + value : value;
                }
                else if (name == "Transactions/sec" || name == "Errors/sec")
                {
                    values[name] = values.TryGetValue(name, out var cur) ? cur + value : value;
                }
                else
                {
                    values[key] = value;
                }
            }
        }
        return new HealthCounterSample { Utc = DateTime.UtcNow, Values = values };
    }

    private static List<HealthProcessRow> ReadProcesses(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = new SqlCommand("""
            SELECT r.session_id,
                   ISNULL(r.blocking_session_id, 0) AS blocking_session_id,
                   ISNULL(DB_NAME(r.database_id), '') AS db_name,
                   r.status, r.command,
                   ISNULL(r.wait_type, '') AS wait_type,
                   ISNULL(r.wait_time, 0) AS wait_ms,
                   r.cpu_time, r.logical_reads, r.reads, r.writes, r.total_elapsed_time,
                   r.granted_query_memory * 8 AS granted_kb,
                   s.login_name, ISNULL(s.host_name, '') AS host_name, ISNULL(s.program_name, '') AS program_name,
                   CASE WHEN t.text IS NULL THEN ''
                        ELSE SUBSTRING(t.text,
                             (ISNULL(r.statement_start_offset, 0) / 2) + 1,
                             ((CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(t.text)
                                   ELSE r.statement_end_offset END
                               - ISNULL(r.statement_start_offset, 0)) / 2) + 1)
                   END AS stmt_text
            FROM sys.dm_exec_requests AS r
            JOIN sys.dm_exec_sessions AS s ON s.session_id = r.session_id
            OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
            WHERE s.is_user_process = 1 AND r.session_id <> @@SPID
            ORDER BY r.session_id
            """, conn) { CommandTimeout = 15 };
        return ReadList(cmd, r => new HealthProcessRow
        {
            SessionId = r["session_id"] as short? ?? 0,
            BlockingSessionId = r["blocking_session_id"] as short? ?? 0,
            Database = r["db_name"] as string ?? "",
            Status = r["status"] as string ?? "",
            Command = r["command"] as string ?? "",
            WaitType = r["wait_type"] as string ?? "",
            WaitMs = r["wait_ms"] as int? ?? 0,
            CpuMs = r["cpu_time"] as int? ?? 0,
            LogicalReads = r["logical_reads"] as long? ?? 0,
            Reads = r["reads"] as long? ?? 0,
            Writes = r["writes"] as long? ?? 0,
            ElapsedMs = r["total_elapsed_time"] as int? ?? 0,
            GrantedMemoryKb = r["granted_kb"] as int? ?? 0,
            Login = r["login_name"] as string ?? "",
            Host = r["host_name"] as string ?? "",
            Program = r["program_name"] as string ?? "",
            Statement = Truncate(r["stmt_text"] as string ?? "", 400)
        }, ct);
    }

    private static List<string> ReadDatabases(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = new SqlCommand(
            "SELECT name FROM sys.databases WHERE state_desc = 'ONLINE' ORDER BY name", conn) { CommandTimeout = 15 };
        return ReadList(cmd, r => r["name"] as string ?? "", ct);
    }

    private static readonly HashSet<string> BenignWaits = new(StringComparer.OrdinalIgnoreCase)
    {
        "SLEEP_TASK", "SLEEP_SYSTEMTASK", "SLEEP_BPOOL_FLUSH", "SLEEP_BUFFERPOOL_HELPLW",
        "SLEEP_DBSTARTUP", "SLEEP_DCOMSTARTUP", "SLEEP_MASTERDBREADY", "SLEEP_MASTERMDREADY",
        "SLEEP_MASTERUPGRADED", "SLEEP_MSDBSTARTUP", "SLEEP_TEMPDBSTARTUP", "SLEEP_TASK",
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
        "HADR_FILESTREAM_IOMGR_IOCOMPLETION", "PVS_PREALLOCATE"
    };

    private static List<HealthWaitRow> ReadWaits(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP (30) wait_type,
                   wait_time_ms / 1000.0 AS wait_s,
                   signal_wait_time_ms / 1000.0 AS signal_s,
                   waiting_tasks_count
            FROM sys.dm_os_wait_stats
            WHERE wait_time_ms > 0
            ORDER BY wait_time_ms DESC
            """, conn) { CommandTimeout = 15 };
        var rows = ReadList(cmd, r => new HealthWaitRow
        {
            WaitType = r["wait_type"] as string ?? "",
            WaitSeconds = Convert.ToDouble(r["wait_s"]),
            SignalSeconds = Convert.ToDouble(r["signal_s"]),
            WaitingTasks = r["waiting_tasks_count"] as long? ?? 0
        }, ct);
        return rows.Where(w => !BenignWaits.Contains(w.WaitType)).ToList();
    }

    private static List<HealthExpensiveQueryRow> ReadExpensiveQueries(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP (50)
                   SUBSTRING(st.text,
                       (ISNULL(qs.statement_start_offset, 0) / 2) + 1,
                       ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text)
                             ELSE qs.statement_end_offset END
                         - ISNULL(qs.statement_start_offset, 0)) / 2) + 1) AS stmt_text,
                   ISNULL(DB_NAME(COALESCE(pl.dbid, st.dbid)), '') AS db_name,
                   qs.execution_count,
                   qs.total_worker_time / 1000.0 AS total_cpu_ms,
                   qs.total_elapsed_time / 1000.0 AS total_elapsed_ms,
                   qs.total_logical_reads,
                   qs.last_execution_time
            FROM sys.dm_exec_query_stats AS qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
            OUTER APPLY sys.dm_exec_query_plan(qs.plan_handle) AS pl
            ORDER BY qs.total_worker_time DESC
            """, conn) { CommandTimeout = 25 };
        return ReadList(cmd, r =>
        {
            var execCount = r["execution_count"] as long? ?? 0;
            var totalCpu = Convert.ToDouble(r["total_cpu_ms"]);
            var totalElapsed = Convert.ToDouble(r["total_elapsed_ms"]);
            var totalReads = r["total_logical_reads"] as long? ?? 0;
            return new HealthExpensiveQueryRow
            {
                Statement = Truncate(r["stmt_text"] as string ?? "", 2000),
                Database = r["db_name"] as string ?? "",
                ExecutionCount = execCount,
                TotalCpuMs = totalCpu,
                AvgCpuMs = execCount > 0 ? totalCpu / execCount : 0,
                TotalElapsedMs = totalElapsed,
                AvgElapsedMs = execCount > 0 ? totalElapsed / execCount : 0,
                TotalReads = totalReads,
                AvgReads = execCount > 0 ? (double)totalReads / execCount : 0,
                LastExecution = r["last_execution_time"] is DateTime le ? le : null
            };
        }, ct);
    }

    /// <summary>Extracts deadlock graphs from the system_health ring buffer (the
    /// store SSMS reads when you "view deadlocks" via extended events).</summary>
    private static List<HealthDeadlockReport> ReadDeadlocks(SqlConnection conn, CancellationToken ct)
    {
        string xml;
        using (var cmd = new SqlCommand("""
            SELECT CAST(xet.target_data AS nvarchar(max)) AS target_data
            FROM sys.dm_xe_session_targets AS xet
            JOIN sys.dm_xe_sessions AS xes ON xes.address = xet.event_session_address
            WHERE xes.name = N'system_health' AND xet.target_name = N'ring_buffer'
            """, conn) { CommandTimeout = 20 })
        {
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return [];
            xml = reader["target_data"] as string ?? "";
        }
        if (xml.Length == 0) return [];

        var root = XDocument.Parse(xml).Root;
        var reports = new List<HealthDeadlockReport>();
        if (root is null) return reports;

        foreach (var ev in root.Descendants("event").Where(e => (string?)e.Attribute("name") == "xml_deadlock_report").TakeLast(30).Reverse())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var timeUtc = DateTime.MinValue;
                if (DateTime.TryParse(ev.Attribute("timestamp")?.Value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                    timeUtc = parsed;

                var value = ev.Element("data")?.Element("value");
                var deadlock = value?.Element("deadlock")
                               ?? XDocument.Parse(value?.Value ?? "<deadlock/>").Root;
                if (deadlock is null) continue;

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

                reports.Add(new HealthDeadlockReport
                {
                    TimeUtc = timeUtc,
                    VictimSpid = processes.FirstOrDefault(p => p.IsVictim)?.Spid ?? "",
                    Processes = processes,
                    RawXml = deadlock.ToString()
                });
            }
            catch (XmlException) { /* one corrupt event must not drop the rest */ }
        }
        return reports;
    }

    private static List<HealthFileIoRow> ReadFileIo(SqlConnection conn, CancellationToken ct)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP (60)
                   DB_NAME(vfs.database_id) AS db_name,
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
            """, conn) { CommandTimeout = 15 };
        return ReadList(cmd, r => new HealthFileIoRow
        {
            Database = r["db_name"] as string ?? "",
            File = r["file_name"] as string ?? "",
            TypeDesc = r["type_desc"] as string ?? "",
            SizeMb = Convert.ToDouble(r["size_mb"]),
            NumOfReads = r["num_of_reads"] as long? ?? 0,
            NumOfWrites = r["num_of_writes"] as long? ?? 0,
            ReadStallSec = Convert.ToDouble(r["read_stall_s"]),
            WriteStallSec = Convert.ToDouble(r["write_stall_s"]),
            AvgStallMs = Convert.ToDouble(r["avg_stall_ms"])
        }, ct);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + " ...";

    public void Dispose() => _conn?.Dispose();
}
