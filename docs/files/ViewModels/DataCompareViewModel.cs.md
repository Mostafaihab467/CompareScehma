# `ViewModels/DataCompareViewModel.cs`

**Purpose:** Data compare view-model: two connections, table discovery, the row-level comparison run, and the save/copy of the script it never executes.

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `class DataCompareViewModel` : ObservableObject — View-model of the data compare window: two connections, the tables both sides share, the row-level verdict per table and the synchronization script that follows from it.

## Public surface

`SavedConnections`, `Tables`, `SelectedRows`, `AuthMethods`, `RowLimitOptions`, `DiscoverCommand`, `CompareCommand`, `GenerateScriptCommand`, `CopyScriptCommand`, `SaveScriptCommand`, `PickSaveFileAsync`, `CopyToClipboardAsync`, `InitializeFrom()`

## Referenced by

- `Views/DataCompareWindow.axaml.cs`
- `Views/MainWindow.axaml.cs`

**Size:** 314 lines
