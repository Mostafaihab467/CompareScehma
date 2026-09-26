# `Services/DatabaseBackupService.cs`

**Purpose:** Native BACKUP DATABASE/LOG plus VERIFYONLY, and the older DacFx BACPAC export; its SQL71562 detection is shared with the snapshot capture.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class DatabaseBackupService` — Creates a portable local BACPAC from any reachable SQL Server database.

## Public surface

`DatabaseBackupService`, `ExportBacpacAsync()`, `BackupAsync()`

## Referenced by

- `Services/SchemaSnapshotService.cs`
- `ViewModels/DbManagerViewModel.cs`
- `ViewModels/MainViewModel.cs`

**Size:** 82 lines
