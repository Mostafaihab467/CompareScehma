# `ViewModels/DbManagerViewModel.cs`

**Purpose:** Object Explorer view-model: server and database tree loading, filters, scripting, database switching, restore/backup, Agent job and new-object designer flows, all fail-closed when a dialog host is missing. ReloadFolderAsync re-queries a folder — including the eagerly-filled object folders, which have no Loader — so the tree shows what a create or drop just did to the server.

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `enum ManagerTab`
- `class DbManagerViewModel` : ObservableObject

## Public surface

`ManagerTab`, `SavedConnections`, `ManagerTreeRoots`, `ObjectTypeFilterOptions`, `ClearExplorerFilterCommand`, `TableColumnNames`, `TableRows`, `TableFilters`, `SaveTableChangesCommand`, `DeleteRowCommand`, `StructureColumns`, `ProcParams`, `ExecResultRows`, `ExecResultColumns`, `CopyToClipboardAsync`, `ConnectCommand`, `RefreshCommand`, `ApplyFiltersCommand`, `AddFilterCommand`, `RemoveFilterCommand`, `ClearFiltersCommand`, `ExecuteProcCommand`, `CopyDefinitionCommand`, `SaveDefinitionCommand`, `SwitchToDataTabCommand`, `SwitchToStructureTabCommand`, `SwitchToDefinitionTabCommand`, `SwitchToExecuteTabCommand`, `SearchCommand`, `ClearSearchCommand`, `OpenNodeCommand`, `DoubleTapNodeCommand`, `TablePropertiesCommand`, `ViewDependenciesCommand`, `SelectTopRowsCommand`, `EditTopRowsCommand`, `NewIndexCommand`, `NewObjectCommand`, `CreatePartitionCommand`, `ScriptCreateCommand`, `ScriptSelectCommand`, `ScriptInsertCommand`, `ScriptUpdateCommand`, `ScriptDeleteCommand`, `ScriptExecCommand`, `ScriptCreateOrAlterCommand`, `ScriptDropCommand`, `RebuildIndexCommand`, `DisableIndexCommand`, `EnableIndexCommand`, `DropIndexCommand`, `UpdateStatsCommand`, `RefreshNodeCommand`, `DatabasePropertiesCommand`, `ShrinkDatabaseCommand`, `RestoreDatabaseCommand`, `BackupDatabaseCommand`, `UseAsCurrentDatabaseCommand`, `StartAgentJobCommand`, `ToggleAgentJobCommand` (+3 more)

## Referenced by

- `Views/DbManagerWindow.axaml`
- `Views/DbManagerWindow.axaml.cs`

**Size:** 2390 lines
