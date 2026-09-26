# `Controls/EditorFolding.cs`

**Purpose:** T-SQL folding: single-pass scanner for BEGIN/END, transactions, batches, comments, parens, plus FoldingManager glue.

**Namespace:** `SchemaCompare.Controls`

## Declared types

- `class EditorFolding` — Collapsible regions for a AvaloniaEdit : block comments, T-SQL BEGIN…END / BEGIN TRANSACTION blocks, GO-separated batches and multi-line parenthesised expressions.

## Public surface

`EditorFolding`, `HandleKeyDown()`, `ToggleCurrent()`, `FoldAll()`, `UnfoldAll()`, `Rebuild()`, `Detach()`

## Referenced by

- `Controls/SqlHighlightedEditor.cs`

**Size:** 317 lines
