# `Services/AppLog.cs`

**Purpose:** Rotating plain-text log; AppLog.RedirectDirectory is how the harness keeps probes out of the real log.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class AppLog` — Plain-text application log in the data folder, size-rotated so a long-lived session cannot fill the disk.

## Public surface

`AppLog`, `WriteFailure`, `RedirectDirectory`, `Info()`, `Warn()`, `Error()`, `Fatal()`, `ExistingLogFiles()`

## Referenced by

- `Program.cs`
- `Services/AppInfo.cs`
- `Services/ClipboardGuard.cs`
- `Services/ClipboardSafety.cs`
- `Services/CsvImportService.cs`
- `Services/QueryBuilderService.cs`
- `Services/QueryHistoryService.cs`
- `Services/RecentFilesService.cs`
- `Services/SnapshotLibraryService.cs`
- `Services/TabSessionService.cs`
- `ViewModels/DataCompareViewModel.cs`
- `ViewModels/DbHealthViewModel.cs`
- `ViewModels/DbManagerViewModel.cs`
- `ViewModels/DiagramViewModel.cs`
- `ViewModels/ImportWizardViewModel.cs`
- `ViewModels/MainViewModel.cs`
- `ViewModels/QueryBuilderViewModel.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/AboutWindow.axaml.cs`
- `Views/DataCompareWindow.axaml.cs`
- `Views/DbManagerWindow.axaml.cs`
- `Views/DiagramWindow.axaml.cs`
- `Views/MainWindow.axaml.cs`
- `Views/QueryWindow.axaml.cs`

**Size:** 90 lines
