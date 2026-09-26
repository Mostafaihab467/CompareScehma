# `Models/DataCompareModels.cs`

**Purpose:** Data compare records: the per-table entry with its discovered key and shared columns, one differing row, and a table's diff with its insert/update/delete statements.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `enum TableDataState` — What a row-level, key-based comparison concluded about one table.
- `enum RowDiffKind`
- `record RowDiff` (RowDiffKind Kind, string KeyValue, string Detail) — One differing row, described by its key.
- `class DataCompareTable` : ObservableObject — A table the comparison can consider, with its verdict once run.
- `class TableDataDiff` — The outcome of comparing one table, including the statements that would synchronize the target.

## Public surface

`TableDataState`, `RowDiffKind`, `RowDiff()`, `Schema`, `Name`, `KeyColumns`, `CompareColumns`, `SkipReason`, `PartialColumns`, `State`, `SourceRows`, `TargetRows`, `MissingInTarget`, `ExtraInTarget`, `ChangedRows`, `TableDataDiff`, `Table`, `Truncated`, `Rows`, `Inserts`, `Updates`, `Deletes`

## Referenced by

- `Services/DataCompareService.cs`
- `ViewModels/DataCompareViewModel.cs`
- `Views/DataCompareWindow.axaml`

**Size:** 141 lines
