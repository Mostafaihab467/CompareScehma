# `ViewModels/DbHealthViewModel.cs`

**Purpose:** DB Health view-model: polling, samples, blocking chain, KILL, error log tail and the Query Store tab (state banner, regressed and top queries, plan list, force/unforce behind the confirmation host).

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `class DbHealthViewModel` : ObservableObject — View-model for the DB Health window (SSMS Activity Monitor style).

## Public surface

`SavedConnections`, `IntervalOptions`, `WaitTopOptions`, `ExpensiveSortOptions`, `ExpensiveTopOptions`, `DatabaseOptions`, `Processes`, `FilteredProcesses`, `Waits`, `ExpensiveQueries`, `Deadlocks`, `FileIo`, `MissingIndexes`, `BlockingChain`, `ErrorLog`, `ErrorLogTailOptions`, `QueryStoreWindowOptions`, `QueryStoreRankOptions`, `QueryStoreRegressed`, `QueryStoreTop`, `QueryStorePlans`, `ConnectCommand`, `RefreshNowCommand`, `TogglePauseCommand`, `RefreshDeadlocksCommand`, `CopyErrorCommand`, `CopyMissingIndexScriptCommand`, `ShowBlockingChainCommand`, `KillSessionCommand`, `LoadErrorLogCommand`, `LoadQueryStoreCommand`, `ForcePlanCommand`, `UnforcePlanCommand`, `EnableQueryStoreCommand`, `CopyToClipboardAsync`, `ConfirmScriptAsync`, `RefreshTickAsync()`, `Shutdown()`, `QueryStoreChangeOptions`

## Referenced by

- `Views/DbHealthWindow.axaml`
- `Views/DbHealthWindow.axaml.cs`

**Size:** 1013 lines
