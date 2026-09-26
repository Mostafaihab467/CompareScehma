# `Services/ExecutionPlanService.cs`

**Purpose:** Parses ShowPlanXML into the plan model, for both estimated and actual plans.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class ExecutionPlanService` — Parses ShowPlanXML documents (captured via SET STATISTICS XML ON) into an tree of operators.

## Public surface

`ExecutionPlanService`, `IsPlanTable()`, `ExtractPlanXmls()`, `Parse()`

## Referenced by

- `ViewModels/QueryViewModel.cs`

**Size:** 276 lines
