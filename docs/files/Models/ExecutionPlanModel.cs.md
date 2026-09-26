# `Models/ExecutionPlanModel.cs`

**Purpose:** Parsed execution plan: operators, statements, missing-index suggestions, and the actual rows/time/reads an executed plan measured against the optimizer's estimate.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class PlanNode` — One operator box in the execution-plan tree (SSMS-style).
- `class MissingIndexSuggestion` — Missing-index hint from the optimizer: impact + a ready CREATE INDEX script.
- `class PlanStatement` — One statement's plan: a small header + the operator tree beneath it.
- `class ExecutionPlan` — Parsed result of one or more ShowPlanXML documents.

## Public surface

`PlanNode`, `PhysicalOp`, `LogicalOp`, `EstimatedRows`, `SubtreeCost`, `NodeCost`, `EstimatedIo`, `EstimatedCpu`, `EstimatedRowSize`, `ActualRows`, `ActualRowsRead`, `ActualCpuMs`, `ActualTimeMs`, `ActualLogicalReads`, `ActualPhysicalReads`, `Executions`, `IsParallel`, `OrderByDiagnostic`, `CostPercent`, `ObjectName`, `Children`, `Warnings`, `Predicate`, `SortOrder`, `SelfAndDescendants()`, `MissingIndexSuggestion`, `Impact`, `Table`, `KeyColumns`, `IncludedColumns`, `CreateScript`, `PlanStatement`, `StatementType`, `StatementText`, `Root`, `OptimizationLevel`, `DegreeOfParallelism`, `MemoryGrantKb`, `CachedPlanSizeKb`, `ActualElapsedMs`, `MissingIndex`, `ExecutionPlan`, `Statements`, `FormatRows()`

## Referenced by

- `Controls/PlanDiagramControl.cs`
- `Models/QueryTab.cs`
- `Services/ExecutionPlanService.cs`

**Size:** 139 lines
