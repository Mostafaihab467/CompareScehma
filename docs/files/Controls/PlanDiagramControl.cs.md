# `Controls/PlanDiagramControl.cs`

**Purpose:** Execution-plan diagram: left-to-right operator boxes with per-operator icons, details panel and Ctrl+wheel zoom, headed ACTUAL or ESTIMATED because the two answer different questions.

**Namespace:** `SchemaCompare.Controls`

## Declared types

- `class PlanDiagramControl` : ContentControl — Draws an execution plan as a left-to-right SSMS-style operator diagram: each becomes an icon-bearing box (operator, object, estimated rows, cost %) connected by elbow lines, statements stacked in vertical bands.
- `class OperatorIcon` — One glyph per operator family, SSMS-style.

## Public surface

`Plan`, `OperatorIcon`, `IconFor()`

## Referenced by

- `Views/QueryWindow.axaml`

**Size:** 667 lines
