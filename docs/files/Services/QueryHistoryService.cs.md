# `Services/QueryHistoryService.cs`

**Purpose:** Persistent, capped query history store in the data folder.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class QueryHistoryService` — Query history persisted as JSON in the data folder, newest first and capped so the file stays small.

## Public surface

`QueryHistoryService`, `Load()`, `Add()`, `Clear()`

## Referenced by

- `ViewModels/QueryViewModel.cs`

**Size:** 60 lines
