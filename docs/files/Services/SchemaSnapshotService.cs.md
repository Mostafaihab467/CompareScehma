# `Services/SchemaSnapshotService.cs`

**Purpose:** Schema snapshots as .dacpac files: schema-only DAC extract with the SQL71562 verify-off retry, the capture provenance written into and read back out of the package, and the UTC-stamped file name that keeps two captures of one day apart.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class SchemaSnapshotService` — Writes and reads schema snapshots as .dacpac files — a schema-only copy of a database that can be compared later without the server being reachable.

## Public surface

`SchemaSnapshotService`, `SuggestedFileName()`, `CaptureAsync()`, `ReadSnapshot()`

## Referenced by

- `ViewModels/MainViewModel.cs`
- `Views/MainWindow.axaml.cs`

**Size:** 132 lines
