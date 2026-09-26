# `Models/QueryResultTable.cs`

**Purpose:** One result set or affected-rows summary produced by a batch.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class QueryResultTable` — One tabular result produced by a query batch: either a result set (columns + rows) or an affected-rows summary for action statements.

## Public surface

`QueryResultTable`, `Title`, `Columns`, `Rows`, `IsTruncated`, `AffectedRows`, `Elapsed`

## Referenced by

- `Models/QueryTab.cs`
- `Services/ExecutionPlanService.cs`
- `Services/QueryExecutionService.cs`
- `Services/ResultsExportService.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/QueryWindow.axaml`
- `Views/QueryWindow.axaml.cs`

**Size:** 27 lines
