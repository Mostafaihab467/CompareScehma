# `Models/HealthActionsModels.cs`

**Purpose:** Rows for the health actions: error log record and one blocking-chain link.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class HealthErrorLogRow` — One SQL Server error log record, read through xp_readerrorlog.
- `class BlockingChainLink` — One hop in a blocking chain, oldest blocker first.

## Public surface

`HealthErrorLogRow`, `LogDate`, `ProcessInfo`, `Text`, `BlockingChainLink`, `SessionId`, `BlockedBy`, `Database`, `Login`, `Host`, `Program`, `Status`, `WaitType`, `WaitMs`, `Statement`, `IsHeadBlocker`

## Referenced by

- `Services/DbHealthService.cs`
- `ViewModels/DbHealthViewModel.cs`
- `Views/DbHealthWindow.axaml`

**Size:** 66 lines
