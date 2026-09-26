# `Models/ConnectionInfo.cs`

**Purpose:** Connection parameters (server, DB, Windows/SQL/Entra auth, TLS), the connection strings they build, a log-safe label and the sibling-database clone the explorer switches to.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class ConnectionInfo`

## Public surface

`ConnectionInfo`, `Server`, `Database`, `UseWindowsAuth`, `Username`, `Password`, `EncryptConnection`, `TrustServerCertificate`, `Authentication`, `BuildTestConnectionString()`, `ServerConnectionString()`, `ForDatabase()`

## Referenced by

- `Models/SavedConnection.cs`
- `Services/CsvImportService.cs`
- `Services/DataCompareService.cs`
- `Services/DataMoveService.cs`
- `Services/DatabaseBackupService.cs`
- `Services/DbHealthService.cs`
- `Services/DbManagerService.cs`
- `Services/DiagramSchemaService.cs`
- `Services/QueryBuilderService.cs`
- `Services/QueryExecutionService.cs`
- `Services/QuerySchemaService.cs`
- `Services/SchemaCompareService.cs`
- `ViewModels/DataCompareViewModel.cs`
- `ViewModels/DbHealthViewModel.cs`
- `ViewModels/DbManagerViewModel.cs`
- `ViewModels/DiagramViewModel.cs`
- `ViewModels/ImportWizardViewModel.cs`
- `ViewModels/MainViewModel.cs`
- `ViewModels/QueryViewModel.cs`

**Size:** 74 lines
