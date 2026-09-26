# `ViewModels/MainViewModel.cs`

**Purpose:** Launcher view-model: the connection forms, the difference list, and both directions of the deployment script — UP preview and DOWN rollback, one of them shown at a time.

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `class MainViewModel` : ObservableObject

## Public surface

`AuthMethods`, `SavedConnections`, `Differences`, `FilteredDifferences`, `DataMoveTables`, `FilteredDataMoveTables`, `CompareCommand`, `GenerateScriptCommand`, `GenerateRollbackScriptCommand`, `ApplyCommand`, `ConfirmApplyCommand`, `CancelApplyCommand`, `TestSourceConnectionCommand`, `TestTargetConnectionCommand`, `SelectAllCommand`, `DeselectAllCommand`, `ClearLogsCommand`, `OpenLogsCommand`, `CloseLogsCommand`, `OpenSettingsCommand`, `CloseSettingsCommand`, `ResetTextSettingsCommand`, `SaveSourceProfileCommand`, `SaveTargetProfileCommand`, `DeleteSavedProfileCommand`, `OpenSchemaCompareCommand`, `OpenMoveDataCommand`, `OpenDataCompareCommand`, `OpenImportWizardCommand`, `OpenBackupCommand`, `OpenDiagramCommand`, `OpenDbManagerCommand`, `OpenQueryCommand`, `OpenDbHealthCommand`, `OpenAboutCommand`, `ExportBackupCommand`, `ToggleSidebarCommand`, `CopyErrorCommand`, `AnalyzeDataMoveCommand`, `StartDataMoveCommand`, `ConfirmDataMoveCommand`, `CancelDataMoveCommand`, `SelectAllDataTablesCommand`, `SelectNoDataTablesCommand`, `OpenMoveDataWindowAction`, `OpenDataCompareWindowAction`, `OpenImportWindowAction`, `OpenBackupWindowAction`, `OpenDiagramWindowAction`, `OpenDbManagerWindowAction`, `OpenQueryWindowAction`, `OpenDbHealthWindowAction`, `OpenAboutWindowAction`, `CopyToClipboardAsync`, `CopyLogsToClipboardAsync()`, `CurrentSourceConnection()`, `CurrentTargetConnection()`

## Referenced by

- `Views/BackupWindow.axaml.cs`
- `Views/MainWindow.axaml`
- `Views/MainWindow.axaml.cs`
- `Views/MoveDataWindow.axaml.cs`

**Size:** 990 lines
