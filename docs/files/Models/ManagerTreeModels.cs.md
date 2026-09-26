# `Models/ManagerTreeModels.cs`

**Purpose:** Object Explorer node model: NodeKind for both the database and the server scope, ManagerNode with its lazy loader and context-menu flags, ExplorerQueryFilter and the index/key metadata records.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `enum NodeKind` — Kind of node rendered in the DB Manager explorer tree.
- `class ManagerNode` : ObservableObject — A single node of the DB Manager explorer tree (SSMS-style).
- `record ExplorerQueryFilter` (string? NamePattern, string? SchemaPattern, string? TypeLabel) — Server-side Object Explorer filter.
- `record MetaKey` (string Name, string Kind /* PK | UQ | FK */, List<string> Columns, string? ReferencedTable)
- `record MetaIndex` (     string Name, string TypeDesc, bool IsUnique, bool IsPrimaryKey, bool IsUniqueConstraint,     bool IsDisabled, string? Filter, List<string> KeyColumns, List<string> IncludedColumns)
- `record MetaStat` (string Name, long Rows, DateTime? LastUpdated)
- `record MetaPartition` (int Number, long Rows, string? FunctionName, string? SchemeName,                             string? Boundary, string? Filegroup)
- `class TableMetadata`

## Public surface

`NodeKind`, `Kind`, `Icon`, `Schema`, `Name`, `IsPrimaryKey`, `IsForeignKey`, `ColumnType`, `IndexType`, `IsUnique`, `IsDisabled`, `IsPrimaryKeyIndex`, `IndexFilter`, `JobEnabled`, `Children`, `Loader`, `HasLoaded`, `Parent`, `RunLoaderNow()`, `ToString()`, `ExplorerQueryFilter()`, `MetaKey()`, `MetaIndex()`, `MetaStat()`, `MetaPartition()`, `TableMetadata`, `Columns`, `Keys`, `Indexes`, `Triggers`, `Stats`, `Partitions`

## Referenced by

- `Models/TablePropertiesModels.cs`
- `Services/DbManagerService.cs`
- `ViewModels/DbManagerViewModel.cs`
- `Views/CreatePartitionDialog.axaml.cs`
- `Views/DbManagerWindow.axaml.cs`
- `Views/NewIndexDialog.axaml.cs`

**Size:** 191 lines
