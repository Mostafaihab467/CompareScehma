# `ViewModels/CommandPaletteViewModel.cs`

**Purpose:** The palette's list: re-filter and re-rank the whole pool on every keystroke, keep the first row selected so Enter always has a target, and refuse an accept that matches nothing or that nothing can receive.

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `class CommandPaletteViewModel` : ObservableObject — The list behind the command palette: every row the catalog and the query window's own commands offered, re-ranked on each keystroke.

## Public surface

`Results`, `Accepted`

## Referenced by

- `Views/CommandPaletteWindow.axaml.cs`

**Size:** 66 lines
