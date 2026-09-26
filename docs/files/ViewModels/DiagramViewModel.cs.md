# `ViewModels/DiagramViewModel.cs`

**Purpose:** Diagram view-model: schema load, table search, layout, persistence.

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `class DiagramViewModel` : ObservableObject — View-model for the DB diagram window: dedicated connection picker, schema loading, search/filter, zoom, auto-layout and layout persistence.

## Public surface

`SavedConnections`, `Tables`, `FilteredTables`, `ActiveTableRelations`, `Relations`, `AuthMethods`, `PickSaveFileAsync`, `PickOpenFileAsync`, `CopyToClipboardAsync`, `InitializeFrom()`

## Referenced by

- `Controls/DiagramTableCard.axaml.cs`
- `Views/DiagramWindow.axaml`
- `Views/DiagramWindow.axaml.cs`
- `Views/MainWindow.axaml.cs`

**Size:** 828 lines
