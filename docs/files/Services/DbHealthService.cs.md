# `Services/DbHealthService.cs`

**Purpose:** DMV reads for health, blocking chain walk, KILL, the xp_readerrorlog tail, and the Query Store reads (version-tolerant options, regressed halves, ranked top queries, plans per query).

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class DbHealthService` : IDisposable — Reads live server health data from SQL Server DMVs — the same sources SSMS Activity Monitor uses — plus deadlock graphs from system_health.

## Public surface

`CollectAsync()`, `ReadErrorLogAsync()`, `KillSessionAsync()`, `DescribeBlockingChain()`, `BlockedSessions()`, `GetQueryStoreStateAsync()`, `GetRegressedQueriesAsync()`, `GetTopQueriesAsync()`, `GetQueryPlansAsync()`, `RunQueryStoreScriptAsync()`, `Dispose()`

## Referenced by

- `ViewModels/DbHealthViewModel.cs`

**Size:** 1229 lines
