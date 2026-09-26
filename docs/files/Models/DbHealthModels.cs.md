# `Models/DbHealthModels.cs`

**Purpose:** Health rows from DMVs: CPU samples, processes, waits, counters, missing indexes, deadlocks, plus QueryStoreState (option banner text) and the regressed/top/plan rows.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class CpuSample` — One point of SQL Server CPU utilization history (from the scheduler monitor ring buffer — the same source SSMS Activity Monitor graphs use).
- `class HealthServerInfo`
- `class HealthCounterSample` — A SQL Server perf counter sample.
- `class HealthProcessRow`
- `class HealthWaitRow`
- `class HealthExpensiveQueryRow`
- `class HealthFileIoRow`
- `class HealthDeadlockProcess` — One process (participant) inside a deadlock graph.
- `class HealthDeadlockReport` — A parsed deadlock graph from the system_health extended-events ring buffer.
- `class HealthMissingIndexRow` — One missing-index recommendation from the optimizer DMVs — the same numbers SSMS shows in the "Missing Indexes Details" standard report.
- `class DbFileInfo` — One file of the database (used by the DB Manager properties dialog).
- `class DatabaseProperties` — Snapshot of one database's core properties (SSMS Properties dialog).
- `class DbPrincipalRow` — One database user or role (read-only, Security folder in DB Manager).
- `class DbHealthSnapshot` — Everything one refresh pass produced.
- `class QueryStoreState` — What sys.database_query_store_options says about one database.
- `class QueryStoreRegressedRow` — One query whose average duration in the second half of the window is worse than in the first half — the "Regressed Queries" report.
- `class QueryStoreTopRow` — One query/plan pair ranked by a resource the operator picked — the "Top Resource Consuming Queries" report.
- `class QueryStorePlanRow` — Every plan Query Store holds for one query, which is what a plan choice is made from: cost, whether it is already forced, and why forcing failed before.

## Public surface

`CpuSample`, `Time`, `SqlPercent`, `IdlePercent`, `OtherPercent`, `HealthServerInfo`, `ServerName`, `ProductVersion`, `ProductLevel`, `SqlServerStartTimeUtc`, `CpuCount`, `HyperthreadRatio`, `SchedulerCount`, `PhysicalMemoryKb`, `HealthCounterSample`, `Utc`, `Values`, `HealthProcessRow`, `SessionId`, `BlockingSessionId`, `Database`, `Status`, `Command`, `WaitType`, `WaitMs`, `CpuMs`, `LogicalReads`, `Reads`, `Writes`, `ElapsedMs`, `GrantedMemoryKb`, `Login`, `Host`, `Program`, `Statement`, `HealthWaitRow`, `WaitSeconds`, `SignalSeconds`, `WaitingTasks`, `SharePercent`, `HealthExpensiveQueryRow`, `ExecutionCount`, `TotalCpuMs`, `AvgCpuMs`, `TotalElapsedMs`, `AvgElapsedMs`, `TotalReads`, `AvgReads`, `LastExecution`, `HealthFileIoRow`, `File`, `TypeDesc`, `SizeMb`, `NumOfReads`, `NumOfWrites`, `ReadStallSec`, `WriteStallSec`, `AvgStallMs`, `HealthDeadlockProcess`, `ProcessId` (+105 more)

## Referenced by

- `Services/DbHealthService.cs`
- `Services/DbManagerService.cs`
- `ViewModels/DbHealthViewModel.cs`
- `ViewModels/DbManagerViewModel.cs`
- `Views/DbHealthWindow.axaml`
- `Views/DbPropertiesDialog.axaml`
- `Views/DbPropertiesDialog.axaml.cs`

**Size:** 373 lines
