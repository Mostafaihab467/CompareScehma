# `Models/ResultGridModels.cs`

**Purpose:** Filter and pivot as data: one column's filter (text plus ticked values, and how it describes itself in a status line), one value with how many rows hold it, the aggregate kinds and the pivot request the dialog returns.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class ResultFilter` — What the operator asked one column of a result set to show.
- `record ResultValue` (string Display, int Count, bool IsNull) — One distinct value a column holds, with how many rows hold it.
- `enum ResultAggregateKind` — What a pivot does with the rows of one group.
- `class PivotRequest` — The operator's answer to "group by what, and do what with each group?" — the picker in Views/ResultPivotDialog returns one of these, and nothing else decides the shape of the pivot.
- `class ResultAggregates` — The as the picker shows it — including whether the aggregate needs a number to work on, which is the one thing the UI has to know before it offers SUM on a text column.

## Public surface

`ResultFilter`, `Column`, `Text`, `Values`, `ToString()`, `ResultValue()`, `ResultAggregateKind`, `PivotRequest`, `GroupBy`, `Kind`, `ValueColumn`, `ResultAggregates`, `Label()`, `NeedsNumber()`, `ColumnName()`

## Referenced by

- `Models/QueryResultTable.cs`
- `Services/ResultGridService.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/QueryWindow.axaml.cs`
- `Views/ResultPivotDialog.axaml.cs`

**Size:** 98 lines
