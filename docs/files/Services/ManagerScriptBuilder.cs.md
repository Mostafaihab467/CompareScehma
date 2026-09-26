# `Services/ManagerScriptBuilder.cs`

**Purpose:** Pure T-SQL builders: script-as, index/partition DDL, CreateObject for the designer, restore batches, backup/verify, SQL Agent job calls, the Query Store force/unforce/enable scripts and the EXEC template — preview equals execution, an invalid definition or id throws a one-line reason instead of emitting broken DDL, and a parameter name already carrying its '@' is not given a second one.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class IndexSpec` — Index shape captured by the New Index dialog.
- `class PartitionSpec` — Partition shape captured by the Create Partition dialog.
- `class ManagerScriptBuilder` — Builds the T-SQL behind DB Manager context actions.

## Public surface

`IndexSpec`, `Schema`, `Table`, `IndexName`, `IndexType`, `IsUnique`, `UseFillFactor`, `FillFactor`, `Filter`, `KeyColumns`, `IncludedColumns`, `ExecuteNow`, `PartitionSpec`, `ColumnName`, `ColumnDataType`, `FunctionName`, `SchemeName`, `RangeType`, `Boundaries`, `Filegroup`, `AlignClusteredIndex`, `ExistingClusteredIndexName`, `ExistingClusteredKeyColumns`, `TableIsHeap`, `ManagerScriptBuilder`, `Q()`, `Qual()`, `CreateIndex()`, `RebuildIndex()`, `DisableIndex()`, `EnableIndex()`, `DropIndex()`, `UpdateStatistics()`, `SelectTop()`, `InsertTemplate()`, `UpdateTemplate()`, `DeleteTemplate()`, `ExecTemplate()`, `DropObject()`, `ShrinkDatabase()`, `RestoreDatabase()`, `SuggestedPath()`, `BackupDatabase()`, `VerifyBackup()`, `SuggestedBackupFileName()`, `StartAgentJob()`, `SetAgentJobEnabled()`, `ForceQueryPlan()`, `UnforceQueryPlan()`, `EnableQueryStore()`, `RestoreBatches()`, `ToCreateOrAlter()`, `CreatePartition()`, `CreateObject()`

## Referenced by

- `Models/ObjectDesignerModels.cs`
- `Models/RestoreModels.cs`
- `Services/CommandCatalogService.cs`
- `Services/DatabaseBackupService.cs`
- `Services/DbHealthService.cs`
- `Services/DbManagerService.cs`
- `ViewModels/DbHealthViewModel.cs`
- `ViewModels/DbManagerViewModel.cs`
- `Views/BackupDatabaseDialog.axaml.cs`
- `Views/CreatePartitionDialog.axaml.cs`
- `Views/NewIndexDialog.axaml.cs`
- `Views/ObjectDesignerDialog.axaml.cs`
- `Views/RestoreDatabaseDialog.axaml.cs`

**Size:** 509 lines
