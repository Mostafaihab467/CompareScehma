# `Models/QueryTab.cs`

**Purpose:** One open query tab: text, file path, dirty flag, results, execution state.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class QueryTab` : ObservableObject — One open query tab: editor text, execution state and its result tables.

## Public surface

`IsReloadingFromFile`, `MarkSavedAt()`, `Results`, `Messages`

## Referenced by

- `Controls/SqlHighlightedEditor.cs`
- `Services/TabSessionService.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/QueryWindow.axaml`

**Size:** 85 lines
