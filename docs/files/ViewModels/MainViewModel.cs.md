# `ViewModels/MainViewModel.cs`

**Purpose:** Launcher view-model: the two comparison sides (live database or snapshot file, each capturable, each able to pick a baseline the app remembers), the difference list, and both directions of the deployment script — UP preview and DOWN rollback, one of them shown at a time.

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `class MainViewModel` : ObservableObject

## Public surface

`AuthMethods`, `SavedConnections`, `Differences`, `FilteredDifferences`, `DataMoveTables`, `FilteredDataMoveTables`, `CompareCommand`, `GenerateScriptCommand`, `GenerateRollbackScriptCommand`, `CaptureSourceSnapshotCommand`, `CaptureTargetSnapshotCommand`, `BrowseSourceSnapshotCommand`, `BrowseTargetSnapshotCommand`, `ForgetSourceSnapshotCommand`, `ForgetTargetSnapshotCommand`, `ApplyCommand`, `ConfirmApplyCommand`, `CancelApplyCommand`, `TestSourceConnectionCommand`, `TestTargetConnectionCommand`, `SelectAllCommand`, `DeselectAllCommand`, `ClearLogsCommand`, `OpenLogsCommand`, `CloseLogsCommand`, `OpenSettingsCommand`, `CloseSettingsCommand`, `ResetTextSettingsCommand`, `SaveSourceProfileCommand`, `SaveTargetProfileCommand`, `DeleteSavedProfileCommand`, `OpenSchemaCompareCommand`, `OpenMoveDataCommand`, `OpenDataCompareCommand`, `OpenImportWizardCommand`, `OpenBackupCommand`, `OpenDiagramCommand`, `OpenDbManagerCommand`, `OpenQueryCommand`, `OpenDbHealthCommand`, `OpenAboutCommand`, `ExportBackupCommand`, `ToggleSidebarCommand`, `CopyErrorCommand`, `AnalyzeDataMoveCommand`, `StartDataMoveCommand`, `ConfirmDataMoveCommand`, `CancelDataMoveCommand`, `SelectAllDataTablesCommand`, `SelectNoDataTablesCommand`, `OpenMoveDataWindowAction`, `OpenDataCompareWindowAction`, `OpenImportWindowAction`, `OpenBackupWindowAction`, `OpenDiagramWindowAction`, `OpenDbManagerWindowAction`, `OpenQueryWindowAction`, `OpenDbHealthWindowAction`, `OpenAboutWindowAction`, `CopyToClipboardAsync` (+5 more)

## Referenced by

- `Views/BackupWindow.axaml.cs`
- `Views/MainWindow.axaml`
- `Views/MainWindow.axaml.cs`
- `Views/MoveDataWindow.axaml.cs`

**Size:** 1270 lines
