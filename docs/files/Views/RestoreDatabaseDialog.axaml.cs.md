# `Views/RestoreDatabaseDialog.axaml.cs`

**Purpose:** Restore dialog logic (RestoreDraft, RestoreFileRow): STOPAT validation, relocation re-suggestions, REPLACE gating, warnings that block OK.

**Namespace:** `SchemaCompare.Views`

**Code-behind for:** `Views/RestoreDatabaseDialog.axaml`

## Declared types

- `class RestoreFileRow` : ObservableObject — One row of the file-relocation grid.
- `class RestoreDraft` — Everything the dialog needs that only the service layer can know.
- `class RestoreDatabaseDialog` : Window

## Public surface

`LogicalName`, `TypeText`, `SizeText`, `SourcePath`, `RelocatedPath`, `RestoreDraft`, `BackupPath`, `ConnectedDatabase`, `Sets`, `DataDirectory`, `LogDirectory`, `TargetExists`, `LoadFiles`, `ShowAsync()`

## Referenced by

- `ViewModels/DbManagerViewModel.cs`
- `Views/DbManagerWindow.axaml.cs`
- `Views/RestoreDatabaseDialog.axaml`

**Size:** 231 lines
