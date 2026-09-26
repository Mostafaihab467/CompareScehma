# `Controls/SqlCompletionProvider.cs`

**Purpose:** IntelliSense-style completion data for the editor: keywords, schema objects and column names, plus the ON clause a JOIN is reaching for (SuggestJoins reads the join tail and offers the real foreign key, inserted at the caret so the alias survives).

**Namespace:** `SchemaCompare.Controls`

## Declared types

- `class SqlCompletionProvider` — SSMS-style IntelliSense completion for T-SQL.

## Public surface

`SqlCompletionProvider`, `NotifySchemaChanged()`, `Tables`, `ColumnsByTable`, `ForeignKeys`, `Attach()`, `Detach()`, `Close()`, `Show()`, `SuggestJoins()`, `Text`, `Complete()`

## Referenced by

- `Controls/SqlHighlightedEditor.cs`
- `ViewModels/QueryBuilderViewModel.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/QueryWindow.axaml.cs`

**Size:** 600 lines
