# `Services/CommandCatalogService.cs`

**Purpose:** What the palette offers: one live catalog read of user tables, views and procedures, turned into SELECT / INSERT / EXEC rows — an INSERT only for a table whose columns it actually has, and a procedure's parameter list fetched only when that row is chosen. No DROP row, by design.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class CommandCatalogService` — The command palette's catalog: which objects the palette can name, and the script each row writes when it is accepted.
- `record CatalogObject` (PaletteGroup Group, string Schema, string Name)

## Public surface

`CommandCatalogService`, `CatalogObject()`, `GetObjectsAsync()`, `RowsFor()`

## Referenced by

- `ViewModels/QueryViewModel.cs`

**Size:** 140 lines
