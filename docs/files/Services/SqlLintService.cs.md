# `Services/SqlLintService.cs`

**Purpose:** Editor diagnostics: unmatched delimiters, typos, unknown tables, unqualified columns.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `record struct` SqlLintIssue(int Start, int Length, string Message)
- `class SqlLintService` — Lightweight T-SQL diagnostics for the query editor: unmatched delimiters, common typos, unknown tables, and qualified / unqualified unknown columns.

## Public surface

`SqlLintIssue()`, `SqlLintService`, `Analyze()`

## Referenced by

- `Controls/EditorFindBar.cs`
- `Controls/SqlHighlightedEditor.cs`
- `Services/SavedConnectionsService.cs`
- `Services/SqlFormatter.cs`

**Size:** 493 lines
