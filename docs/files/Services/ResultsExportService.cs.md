# `Services/ResultsExportService.cs`

**Purpose:** Renders results as TSV, CSV, JSON, Markdown or INSERT scripts, and works out the INSERT target from the script.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class ResultsExportService` — Renders a as TSV, CSV, JSON, Markdown or INSERT scripts — SSMS "Copy with Headers" / "Save Results As" equivalents.

## Public surface

`ResultsExportService`, `ToTsv()`, `ToCsv()`, `ToJson()`, `ToMarkdown()`, `ToInsertScripts()`, `InsertTargetFrom()`

## Referenced by

- `ViewModels/QueryViewModel.cs`

**Size:** 207 lines
