# `Services/SavedConnectionsService.cs`

**Purpose:** Reads and writes saved connection profiles, including DPAPI-protected passwords.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class SavedConnectionsService` — Persists saved DB credential profiles to %AppData%/SchemaCompare/saved_connections.json.

## Public surface

`SavedConnectionsService`, `LastWarning`, `Load()`, `Save()`, `Id`, `Name`, `Server`, `Database`, `UseWindowsAuth`, `Username`, `EncryptedPassword`, `EncryptConnection`, `TrustServerCertificate`, `Authentication`, `Protect()`, `Unprotect()`

## Referenced by

- `Models/SavedConnection.cs`
- `ViewModels/DataCompareViewModel.cs`
- `ViewModels/DbHealthViewModel.cs`
- `ViewModels/DbManagerViewModel.cs`
- `ViewModels/DiagramViewModel.cs`
- `ViewModels/ImportWizardViewModel.cs`
- `ViewModels/MainViewModel.cs`
- `ViewModels/QueryViewModel.cs`

**Size:** 221 lines
