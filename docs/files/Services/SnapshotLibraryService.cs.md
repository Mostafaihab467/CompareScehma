# `Services/SnapshotLibraryService.cs`

**Purpose:** The list of baselines this app has captured or opened: newest-first, de-duped by path, pruned of files that no longer exist, and stored as paths and provenance only — forgetting a row never touches the .dacpac.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class SnapshotLibraryService` — The baselines this app has captured or compared against, so "compare against yesterday's snapshot" is a pick from a list rather than a trip through a file dialog to a folder of timestamped files.

## Public surface

`SnapshotLibraryService`, `Entries`, `Load()`, `Remember()`, `Find()`, `Forget()`

## Referenced by

- `ViewModels/MainViewModel.cs`

**Size:** 93 lines
