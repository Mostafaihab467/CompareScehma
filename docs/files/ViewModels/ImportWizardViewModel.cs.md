# `ViewModels/ImportWizardViewModel.cs`

**Purpose:** View-model of the import wizard: read file, review the mapping, match an existing table, then load — failing closed without a confirmation host.

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `class ImportWizardViewModel` : ObservableObject — View-model of the import wizard: a file, a destination database and a table, and the column-by-column mapping between them.

## Public surface

`SavedConnections`, `Columns`, `DelimiterOptions`, `TypeOptions`, `AuthMethods`, `ReadFileCommand`, `PreviewCommand`, `MatchTargetCommand`, `LoadCommand`, `PickOpenFileAsync`, `ShowScriptConfirmAsync`, `InitializeFrom()`

## Referenced by

- `Views/ImportWizardWindow.axaml.cs`
- `Views/MainWindow.axaml.cs`

**Size:** 328 lines
