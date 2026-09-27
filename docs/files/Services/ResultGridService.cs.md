# `Services/ResultGridService.cs`

**Purpose:** The grid's arithmetic, pure: a column's distinct values with their counts (the null and the DBNull sharing one (NULL) entry), the rows a set of filters leaves visible (columns AND, ticks OR, free text that never matches NULL), and a pivot grouped over those visible rows only. An unknown column or a SUM over text throws a one-line reason instead of showing zeros.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class ResultGridService` — The reading end of the result grid: what a column actually holds, which rows a set of filters keeps, and what the same rows look like grouped by one column.

## Public surface

`ResultGridService`, `Display()`, `ValueOf()`, `DistinctValues()`, `VisibleRows()`, `Matches()`, `Pivot()`

## Referenced by

- `Models/QueryResultTable.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/QueryWindow.axaml.cs`
- `Views/ResultPivotDialog.axaml.cs`

**Size:** 275 lines
