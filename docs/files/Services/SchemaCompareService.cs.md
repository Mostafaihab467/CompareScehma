# `Services/SchemaCompareService.cs`

**Purpose:** DacFx schema compare and the one comparison path behind both script directions — forward deploy, reversed rollback — with the function-before-view reorder and the deploy-database retargeting.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class SchemaCompareService`

## Public surface

`SchemaCompareService`, `TestConnectionAsync()`, `GenerateScriptAsync()`, `GenerateRollbackScriptAsync()`, `ReorderScriptBatches()`, `ExtractExecutableBatches()`

## Referenced by

- `Services/DbManagerService.cs`
- `ViewModels/DiagramViewModel.cs`
- `ViewModels/MainViewModel.cs`

**Size:** 454 lines
