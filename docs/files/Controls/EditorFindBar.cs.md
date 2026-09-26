# `Controls/EditorFindBar.cs`

**Purpose:** Find & replace strip (Ctrl+F / Ctrl+H) built in code, with match highlighting and replace-all undo.

**Namespace:** `SchemaCompare.Controls`

## Declared types

- `class EditorFindBar` — Find &amp; replace strip for a AvaloniaEdit , built in code so every SQL surface in the app can share it.
- `record struct` TextLocation(int Offset, int Length)
- `class SqlMatchHighlightRenderer` : IBackgroundRenderer — Paints search hits (amber) and the active hit (blue) behind the text.

## Public surface

`EditorFindBar`, `Open()`, `Close()`, `HandleKeyDown()`, `Rebuild()`, `TextLocation()`, `Matches`, `SelectedIndex`, `Draw()`

## Referenced by

- `Controls/EditorGotoLine.cs`
- `Controls/SqlHighlightedEditor.cs`
- `Models/QueryGuard.cs`
- `Services/SavedConnectionsService.cs`
- `Services/SqlFormatter.cs`
- `Services/SqlLintService.cs`

**Size:** 288 lines
