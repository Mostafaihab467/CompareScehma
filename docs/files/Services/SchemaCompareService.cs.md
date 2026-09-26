# `Services/SchemaCompareService.cs`

**Purpose:** DacFx schema compare over any pair of live databases or snapshot files, and the one comparison path behind both script directions — forward deploy, reversed rollback — with the function-before-view reorder, the deploy-database retargeting and the note a file on either side forces on the script.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class SchemaCompareService`

## Public surface

`SchemaCompareService`, `TestConnectionAsync()`, `GenerateScriptAsync()`, `GenerateRollbackScriptAsync()`, `ReorderScriptBatches()`, `ExtractExecutableBatches()`

## Referenced by

- `Services/DbManagerService.cs`
- `ViewModels/DiagramViewModel.cs`
- `ViewModels/MainViewModel.cs`

**Size:** 501 lines
