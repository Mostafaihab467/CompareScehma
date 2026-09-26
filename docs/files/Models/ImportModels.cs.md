# `Models/ImportModels.cs`

**Purpose:** ImportColumn (one file column and where it maps), TargetColumn, CsvTable and ImportResult for the import wizard.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class ImportColumn` : ObservableObject — One CSV column as the import wizard sees it: where it came from, where it goes, and what the wizard guessed about its type.
- `class TargetColumn` — Columns of an existing target table, read so the mapping can be checked.
- `class CsvTable` — What a CSV file looks like before any of it is sent to a server.
- `class ImportResult` — The outcome of a load: what was written, and whether the table came with it.

## Public surface

`SourceName`, `Position`, `TargetColumn`, `Name`, `Type`, `Nullable`, `IsIdentity`, `IsComputed`, `CsvTable`, `Headers`, `Sample`, `ImportResult`, `RowsLoaded`, `TableCreated`

## Referenced by

- `Services/CsvImportService.cs`
- `ViewModels/ImportWizardViewModel.cs`

**Size:** 63 lines
