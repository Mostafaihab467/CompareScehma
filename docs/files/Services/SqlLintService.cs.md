# `Services/SqlLintService.cs`

**Purpose:** Editor diagnostics: unmatched delimiters, typos, unknown tables, unqualified columns — dotted names match up to four parts and sys / INFORMATION_SCHEMA views are exempt from the unknown-table rule, because catalog views are in every database and in no schema cache.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `record struct` SqlLintIssue(int Start, int Length, string Message)
- `class SqlLintService` — Lightweight T-SQL diagnostics for the query editor: unmatched delimiters, common typos, unknown tables, and qualified / unqualified unknown columns.

## Public surface

`SqlLintIssue()`, `SqlLintService`, `Analyze()`

## Referenced by

- `Controls/EditorFindBar.cs`
- `Controls/SqlHighlightedEditor.cs`
- `Models/JoinSuggestion.cs`
- `Models/QueryGuard.cs`
- `Services/QueryGuardService.cs`
- `Services/SavedConnectionsService.cs`
- `Services/SqlFormatter.cs`

**Size:** 513 lines
