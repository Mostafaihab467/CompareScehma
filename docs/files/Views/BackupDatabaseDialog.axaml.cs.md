# `Views/BackupDatabaseDialog.axaml.cs`

**Purpose:** Backup dialog logic (BackupDraft): server-side default path, backup type, APPEND vs FORMAT, VERIFY ONLY, preview and OK gating.

**Namespace:** `SchemaCompare.Views`

**Code-behind for:** `Views/BackupDatabaseDialog.axaml`

## Declared types

- `class BackupDraft` — Defaults the backup dialog starts from, all read from the server.
- `class BackupDatabaseDialog` : Window

## Public surface

`BackupDraft`, `Database`, `SuggestedFileName`, `DefaultBackupDirectory`, `CanBackUpLog`, `ShowAsync()`

## Referenced by

- `ViewModels/DbManagerViewModel.cs`
- `Views/BackupDatabaseDialog.axaml`
- `Views/DbManagerWindow.axaml.cs`

**Size:** 104 lines
