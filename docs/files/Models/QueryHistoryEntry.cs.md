# `Models/QueryHistoryEntry.cs`

**Purpose:** One persisted history entry: script, timing, server, database, outcome.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class QueryHistoryEntry` — One executed script or selection, kept in the persistent query history so the user can search previous work and drop it back into the editor.

## Public surface

`QueryHistoryEntry`, `Sql`, `ExecutedAt`, `Server`, `Database`, `DurationSeconds`, `ResultSets`, `TotalRows`, `Scope`, `Error`, `Preview`, `Matches()`

## Referenced by

- `Services/QueryHistoryService.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/QueryHistoryWindow.axaml`

**Size:** 63 lines
