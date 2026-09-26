# `Models/ObjectDesignerModels.cs`

**Purpose:** DesignerKind (table, view, procedure), one DesignerColumn per table column and the DesignerSpec the designer hands back with its Execute-now choice.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `enum DesignerKind` — What the object designer is being asked to build.
- `class DesignerColumn` — One column of a table under construction.
- `class DesignerSpec` — The designer's answer: an object definition plus what to do with it.

## Public surface

`DesignerKind`, `DesignerColumn`, `Name`, `Type`, `Nullable`, `IsKey`, `IsIdentity`, `Default`, `DesignerSpec`, `Kind`, `Schema`, `Columns`, `Body`, `ExecuteNow`

## Referenced by

- `Services/ManagerScriptBuilder.cs`
- `ViewModels/DbManagerViewModel.cs`
- `Views/ObjectDesignerDialog.axaml.cs`

**Size:** 41 lines
