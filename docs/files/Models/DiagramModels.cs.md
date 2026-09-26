# `Models/DiagramModels.cs`

**Purpose:** Diagram state: table nodes, columns, relations and the persisted layout.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class DiagramColumn` — One column shown inside a diagram table card.
- `class DiagramTableNode` : ObservableObject — A draggable table node on the diagram canvas.
- `class DiagramRelation` — A foreign-key edge drawn between two table nodes.
- `class DiagramRelationDisplayItem` — Display model for a single foreign-key relation of an active table.
- `class DiagramPersistedState` — Persisted diagram layout: node positions, visibility and zoom.
- `class DiagramTableState`

## Public surface

`DiagramColumn`, `Name`, `DataType`, `IsPrimaryKey`, `IsForeignKey`, `IsNullable`, `IsIdentity`, `Schema`, `Columns`, `RowCount`, `DiagramRelation`, `ConstraintName`, `ChildTable`, `ParentTable`, `ChildColumns`, `ParentColumns`, `DiagramRelationDisplayItem`, `IsOutgoing`, `OtherTable`, `DiagramPersistedState`, `Server`, `Database`, `SavedAtUtc`, `Zoom`, `Tables`, `DiagramTableState`, `FullName`, `X`, `Y`, `IsVisible`

## Referenced by

- `Controls/DiagramTableCard.axaml`
- `Controls/DiagramTableCard.axaml.cs`
- `Services/DiagramAutoLayoutService.cs`
- `Services/DiagramPersistenceService.cs`
- `Services/DiagramSchemaService.cs`
- `ViewModels/DiagramViewModel.cs`
- `Views/DiagramWindow.axaml`
- `Views/DiagramWindow.axaml.cs`

**Size:** 109 lines
