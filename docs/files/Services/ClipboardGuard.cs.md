# `Services/ClipboardGuard.cs`

**Purpose:** Routes an editor's Ctrl+C/X/V through ClipboardSafety so a locked clipboard cannot kill the app.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class ClipboardGuard` — Crash-safe clipboard handling shared by every window of the app.

## Public surface

`ClipboardGuard`, `Attach()`

## Referenced by

- `Views/BackupWindow.axaml.cs`
- `Views/DataCompareWindow.axaml.cs`
- `Views/DiagramWindow.axaml.cs`
- `Views/MainWindow.axaml.cs`
- `Views/MoveDataWindow.axaml.cs`
- `Views/QueryWindow.axaml.cs`

**Size:** 155 lines
