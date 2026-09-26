# `Models/DataMoveTable.cs`

**Purpose:** Data-move model: eligible tables, plan, relation order and per-table result.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class DataMoveTable` : ObservableObject — One table that can safely participate in a data synchronization.
- `class DataMovePlan`
- `class DataMoveRelation` — A target foreign-key rule, including columns needed for source-data validation.
- `record DataMoveResult` (int TablesCompleted, long InsertedRows, long UpdatedRows)

## Public surface

`Schema`, `Name`, `PrimaryKeyColumns`, `WritableColumns`, `SourceRowCount`, `Warning`, `DataMovePlan`, `Tables`, `ParentTables`, `Relations`, `DataMoveRelation`, `ConstraintName`, `ChildTable`, `ParentTable`, `ChildColumns`, `ParentColumns`, `DataMoveResult()`

## Referenced by

- `Services/DataMoveService.cs`
- `ViewModels/MainViewModel.cs`
- `Views/MoveDataWindow.axaml`

**Size:** 39 lines
