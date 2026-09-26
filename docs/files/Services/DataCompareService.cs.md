# `Services/DataCompareService.cs`

**Purpose:** Row-level key comparison: table discovery with shared keys, the streamed row diff with its literal key pairing, value equality by type, and the review-first insert/update/delete script it writes without ever executing SQL against the target.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class DataCompareService` — Row-level, key-based comparison of one table across two connections.

## Public surface

`DataCompareService`, `Columns`, `Types`, `CompareAsync()`, `BuildSyncScript()`, `InsertStatement()`, `UpdateStatement()`, `DeleteStatement()`, `Literal()`, `SameValue()`

## Referenced by

- `ViewModels/DataCompareViewModel.cs`

**Size:** 513 lines
