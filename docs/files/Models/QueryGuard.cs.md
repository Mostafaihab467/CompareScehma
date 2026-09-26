# `Models/QueryGuard.cs`

**Purpose:** What a script will destroy, as data: one finding (level, stable rule id, prose) and the report that says whether it needs a confirmation and what to lead a dialog with.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `enum GuardLevel`
- `record struct` GuardFinding(GuardLevel Level, string Rule, string Message) — One thing a script will destroy.
- `class GuardReport` — What a script does to data, read off the text before the server ever sees it.

## Public surface

`GuardLevel`, `GuardFinding()`, `GuardReport`, `Findings`, `Headline`

## Referenced by

- `Controls/EditorFindBar.cs`
- `Models/JoinSuggestion.cs`
- `Services/QueryGuardService.cs`
- `Services/SavedConnectionsService.cs`
- `Services/SqlFormatter.cs`
- `Services/SqlLintService.cs`
- `ViewModels/QueryViewModel.cs`

**Size:** 43 lines
