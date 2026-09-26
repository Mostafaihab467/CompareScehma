# `Services/CsvImportService.cs`

**Purpose:** Reads a delimited file (RFC 4180 quoting), infers SQL types from a sample, builds the CREATE TABLE and bulk-loads rows in one transaction.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class CsvImportService` — Reads a delimited text file, guesses what each column holds, and loads the rows into SQL Server with .

## Public surface

`CsvImportService`, `readonly()`, `ReadTable()`, `EnumerateRecords()`, `SanitizeName()`, `BuildCreateTable()`, `TableExistsAsync()`, `ReadTargetColumnsAsync()`, `LoadAsync()`, `AddRow()`

## Referenced by

- `ViewModels/ImportWizardViewModel.cs`

**Size:** 456 lines
