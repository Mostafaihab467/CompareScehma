# `Services/SqlExplainerService.cs`

**Purpose:** Deterministic plain-English analysis of a script: steps, joins, effects and safety warnings.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class SqlExplanation` — Structured plain-English analysis for the explain dialog.
- `class SqlExplainerService` — Deterministic (non-AI) plain-English description of a SQL script.

## Public surface

`SqlExplanation`, `Headline`, `Bullets`, `Warnings`, `SqlExplainerService`, `Explain()`, `ExplainDetailed()`

## Referenced by

- `ViewModels/QueryViewModel.cs`
- `Views/QueryExplainDialog.axaml.cs`

**Size:** 766 lines
