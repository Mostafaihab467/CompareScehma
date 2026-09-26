# `Models/SavedConnection.cs`

**Purpose:** A saved connection profile: its authentication method, how its password is held and how it becomes a ConnectionInfo.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class SavedConnection` : ObservableObject — A saved DB credential profile.

## Public surface

`Id`, `BuildDefaultName()`, `Matches()`, `ToConnectionInfo()`

## Referenced by

- `Services/SavedConnectionsService.cs`
- `ViewModels/DataCompareViewModel.cs`
- `ViewModels/DbHealthViewModel.cs`
- `ViewModels/DbManagerViewModel.cs`
- `ViewModels/DiagramViewModel.cs`
- `ViewModels/ImportWizardViewModel.cs`
- `ViewModels/MainViewModel.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/DataCompareWindow.axaml`
- `Views/DbHealthWindow.axaml`
- `Views/DbManagerWindow.axaml`
- `Views/DiagramWindow.axaml`
- `Views/ImportWizardWindow.axaml`
- `Views/MainWindow.axaml`
- `Views/MoveDataWindow.axaml`
- `Views/QueryWindow.axaml`

**Size:** 83 lines
