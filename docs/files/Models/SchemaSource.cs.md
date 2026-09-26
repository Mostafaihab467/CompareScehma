# `Models/SchemaSource.cs`

**Purpose:** One side of a schema comparison as either a live database or a snapshot file, so compare, script and deploy never assume a reachable server.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class SchemaSource` — One side of a schema comparison: either a live database to read or a snapshot file captured earlier.

## Public surface

`SchemaSource`, `Connection`, `SnapshotPath`, `Snapshot`, `OfDatabase()`, `OfSnapshot()`

## Referenced by

- `Models/SnapshotInfo.cs`
- `Services/SchemaCompareService.cs`
- `ViewModels/MainViewModel.cs`

**Size:** 39 lines
