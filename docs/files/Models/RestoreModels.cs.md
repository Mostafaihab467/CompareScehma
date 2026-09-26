# `Models/RestoreModels.cs`

**Purpose:** Restore domain model: BackupSetInfo, BackupFileInfo, RestorePlan and its validation rules.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class BackupSetInfo` — One backup set inside a media file, as reported by RESTORE HEADERONLY.
- `class BackupFileInfo` — One file inside a backup set, as reported by RESTORE FILELISTONLY.
- `class RestorePlan` — Everything needed to emit and run one RESTORE.

## Public surface

`BackupSetInfo`, `Position`, `BackupType`, `DatabaseName`, `BackupStartDate`, `BackupSizeBytes`, `ServerName`, `IsCopyOnly`, `Description`, `BackupFileInfo`, `LogicalName`, `PhysicalName`, `Type`, `SizeBytes`, `MaxSizeBytes`, `GrowthBytes`, `FileGroupName`, `RestorePlan`, `BackupPath`, `SetPosition`, `TargetDatabase`, `SourceDatabase`, `Files`, `Moves`, `StopAt`, `ReplaceExisting`, `NoRecovery`, `Validate()`

## Referenced by

- `Services/DbManagerService.cs`
- `Services/ManagerScriptBuilder.cs`
- `ViewModels/DbManagerViewModel.cs`
- `Views/RestoreDatabaseDialog.axaml.cs`

**Size:** 148 lines
