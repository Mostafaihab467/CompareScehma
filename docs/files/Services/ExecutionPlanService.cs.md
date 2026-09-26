# `Services/ExecutionPlanService.cs`

**Purpose:** Parses ShowPlanXML into the plan model: estimates for a compiled plan, and for a run one the RuntimeCounters under RunTimeInformation (rows and reads summed across threads, elapsed time taken as the maximum) plus a warning wherever the row estimate missed by 10x or more.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class ExecutionPlanService` — Parses ShowPlanXML documents (captured via SET STATISTICS XML ON) into an tree of operators.

## Public surface

`ExecutionPlanService`, `IsPlanTable()`, `ExtractPlanXmls()`, `Parse()`

## Referenced by

- `ViewModels/QueryViewModel.cs`

**Size:** 330 lines
