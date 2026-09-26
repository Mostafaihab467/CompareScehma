# `Models/PaletteItem.cs`

**Purpose:** One palette row: its group (command, table, view, procedure), the title and detail it shows, and either the command to run or the script to build — with the scoring rule that a title match beats a detail match.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `enum PaletteGroup` — What a palette row stands for.
- `class PaletteItem` — One row of the command palette: either an app command to run, or a database object to write a read script for.

## Public surface

`PaletteGroup`, `PaletteItem`, `Title`, `Detail`, `Group`, `Command`, `BuildScriptAsync`, `Score()`, `ToString()`

## Referenced by

- `Services/CommandCatalogService.cs`
- `ViewModels/CommandPaletteViewModel.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/CommandPaletteWindow.axaml`
- `Views/CommandPaletteWindow.axaml.cs`

**Size:** 49 lines
