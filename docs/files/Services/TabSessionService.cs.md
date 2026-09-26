# `Services/TabSessionService.cs`

**Purpose:** Saves and restores the open query tabs across runs.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class TabSessionEntry` — One tab as stored between runs.
- `class TabSessionService` — Persists which query tabs were open, so closing the window does not silently discard them.

## Public surface

`TabSessionEntry`, `Title`, `FilePath`, `SqlText`, `IsActive`, `TabSessionService`, `Load()`, `Save()`, `Clear()`

## Referenced by

- `ViewModels/QueryViewModel.cs`

**Size:** 87 lines
