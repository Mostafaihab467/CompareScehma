# `Controls/SqlHighlightedEditor.cs`

**Purpose:** Shared T-SQL editor control: highlighting, lint squiggles, diff line shading, find/fold/bookmark/goto wiring.

**Namespace:** `SchemaCompare.Controls`

## Declared types

- `class SqlHighlightedEditor` : UserControl — T-SQL editor with VS-style coloring, optional red squiggles for lint issues, and optional green/red line backgrounds for schema-compare diffs.
- `class SqlSquiggleRenderer` : IBackgroundRenderer
- `class SqlLineHighlightRenderer` : IBackgroundRenderer

## Public surface

`Text`, `IsReadOnly`, `EnableLint`, `PartnerText`, `DiffRole`, `LintSummary`, `EnableIntelliSense`, `HandleEditorChord()`, `AttachCompletion()`, `Issues`, `Draw()`, `LineNumbers`, `Fill`, `Border`

## Referenced by

- `Controls/EditorGotoLine.cs`
- `Views/AggregationExplainerDialog.axaml`
- `Views/BackupDatabaseDialog.axaml`
- `Views/CreatePartitionDialog.axaml`
- `Views/DbManagerWindow.axaml`
- `Views/MainWindow.axaml`
- `Views/NewIndexDialog.axaml`
- `Views/ObjectDesignerDialog.axaml`
- `Views/ObjectDesignerDialog.axaml.cs`
- `Views/QueryBuilderWindow.axaml`
- `Views/QueryHistoryWindow.axaml`
- `Views/QueryWindow.axaml`
- `Views/QueryWindow.axaml.cs`
- `Views/RestoreDatabaseDialog.axaml`
- `Views/ScriptActionDialog.axaml`

**Size:** 442 lines
