# `Models/QueryResultTable.cs`

**Purpose:** One result set or affected-rows summary produced by a batch, plus what the operator has narrowed it to: the active column filters and the VisibleRows the grid, the copy and every export read.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class QueryResultTable` : ObservableObject — One tabular result produced by a query batch: either a result set (columns + rows) or an affected-rows summary for action statements.

## Public surface

`Title`, `Columns`, `Rows`, `IsTruncated`, `AffectedRows`, `Elapsed`, `Filters`, `ApplyFilters()`, `ClearFilters()`, `FilterFor()`

## Referenced by

- `Models/QueryTab.cs`
- `Services/ExecutionPlanService.cs`
- `Services/QueryExecutionService.cs`
- `Services/ResultGridService.cs`
- `Services/ResultsExportService.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/QueryWindow.axaml`
- `Views/QueryWindow.axaml.cs`
- `Views/ResultPivotDialog.axaml.cs`

**Size:** 105 lines
