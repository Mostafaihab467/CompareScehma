# `Services/DbManagerService.cs`

**Purpose:** Data access for Object Explorer: object and server-level browsing (databases, logins, linked servers, Agent jobs), object scripting, restore media reads, table/database properties.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class DbManagerService` — Data-access layer for the DB Manager window.

## Public surface

`DbManagerService`, `GetObjectsAsync()`, `SearchObjectsAsync()`, `GetTableColumnsAsync()`, `GetTableMetadataAsync()`, `GetObjectDefinitionAsync()`, `ExecuteRawScriptAsync()`, `GetProcedureParamsAsync()`, `UpdateRowAsync()`, `InsertRowAsync()`, `DeleteRowAsync()`, `GetDatabasePropertiesAsync()`, `GetServerOverviewAsync()`, `GetDatabasesAsync()`, `GetLinkedServersAsync()`, `GetAgentJobsAsync()`, `GetDependenciesAsync()`, `ReadBackupSetsAsync()`, `ReadBackupFilesAsync()`, `DatabaseExistsAsync()`, `RestoreDatabaseAsync()`, `GetRowCountAsync()`, `GetTablePropertiesAsync()`

## Referenced by

- `Models/ManagerTreeModels.cs`
- `Models/ServerBrowserModels.cs`
- `Models/TablePropertiesModels.cs`
- `ViewModels/DbManagerViewModel.cs`

**Size:** 1945 lines
