# `Models/ObjectDependencyModels.cs`

**Purpose:** Object dependency graph: one Uses/Used-by edge with its sys type_desc translated for a DBA, and the whole set of neighbours around one catalog object.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `record DependencyRow` (string Direction, string ObjectName, string ObjectType, string Detail) — One edge of the dependency graph around a selected object.
- `class ObjectDependencies` — Everything the catalog says about one object's neighbours.

## Public surface

`DependencyRow()`, `FriendlyKind()`, `ObjectDependencies`, `Schema`, `Name`, `TypeDesc`, `Rows`, `Uses`, `UsedBy`

## Referenced by

- `Services/DbManagerService.cs`
- `ViewModels/DbManagerViewModel.cs`
- `Views/DependenciesDialog.axaml.cs`

**Size:** 76 lines
