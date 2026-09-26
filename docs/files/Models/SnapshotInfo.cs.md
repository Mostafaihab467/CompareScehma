# `Models/SnapshotInfo.cs`

**Purpose:** What a .dacpac snapshot says about itself: the database and server it was captured from, when, and how old that makes it.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class SnapshotInfo` — What a that points at a snapshot file knows about itself.

## Public surface

`SnapshotInfo`, `Path`, `FileName`, `CapturedDatabase`, `CapturedServer`, `CapturedAt`, `FileSizeBytes`, `Caption`, `DescribeAge()`

## Referenced by

- `Models/SchemaSource.cs`
- `Models/SnapshotEntry.cs`
- `Services/SchemaSnapshotService.cs`
- `Services/SnapshotLibraryService.cs`
- `ViewModels/MainViewModel.cs`

**Size:** 61 lines
