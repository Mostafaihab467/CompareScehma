# `Models/TablePropertiesModels.cs`

**Purpose:** Read-only per-table properties snapshot: space, columns, PK, FKs both ways, checks, indexes, triggers, stats, partitions.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class TableProperties` — Read-only per-table properties snapshot backing TablePropertiesDialog (DbManagerService.GetTablePropertiesAsync).
- `class TablePkInfo`
- `class TableForeignKeyRow` — One foreign key.
- `class TableCheckRow`
- `class TableIndexRow`
- `class TableTriggerRow`
- `class TableStatRow`
- `class TablePartitionInfo`

## Public surface

`TableProperties`, `Schema`, `Name`, `Rows`, `ReservedKb`, `DataKb`, `IndexKb`, `UnusedKb`, `Columns`, `PrimaryKey`, `ForeignKeys`, `ReferencedBy`, `Checks`, `Indexes`, `Triggers`, `Statistics`, `Partitioning`, `Partitions`, `TablePkInfo`, `TableForeignKeyRow`, `ReferencedTable`, `ReferencedColumns`, `ParentTable`, `ParentColumns`, `TableCheckRow`, `Definition`, `TableIndexRow`, `TypeDesc`, `IsUnique`, `IsPrimaryKey`, `IsDisabled`, `KeyColumns`, `IncludedColumns`, `UserSeeks`, `UserScans`, `UserLookups`, `UserUpdates`, `TableTriggerRow`, `Timing`, `Events`, `TableStatRow`, `Filtered`, `LastUpdated`, `TablePartitionInfo`, `SchemeName`, `FunctionName`, `FunctionTypeDesc`

## Referenced by

- `Services/DbManagerService.cs`
- `ViewModels/DbManagerViewModel.cs`
- `Views/TablePropertiesDialog.axaml`
- `Views/TablePropertiesDialog.axaml.cs`

**Size:** 113 lines
