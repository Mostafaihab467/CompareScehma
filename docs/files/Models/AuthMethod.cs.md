# `Models/AuthMethod.cs`

**Purpose:** The authentication dropdown: Windows, SQL and the Entra ID flows, each mapped to the SqlClient Authentication= value it emits.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `record AuthMethod` (     string Display,     string AuthenticationMethod,     bool IsWindows,     bool NeedsCredentials,     string CredentialHint) — One option of the authentication dropdown.

## Public surface

`AuthMethod()`, `All`, `For()`, `MethodNeedsCredentials()`, `MethodForcesEncryption()`, `ToString()`

## Referenced by

- `Models/ConnectionInfo.cs`
- `Models/SavedConnection.cs`
- `ViewModels/DataCompareViewModel.cs`
- `ViewModels/DiagramViewModel.cs`
- `ViewModels/ImportWizardViewModel.cs`
- `ViewModels/MainViewModel.cs`
- `Views/DataCompareWindow.axaml`
- `Views/DiagramWindow.axaml`
- `Views/ImportWizardWindow.axaml`
- `Views/MainWindow.axaml`
- `Views/MoveDataWindow.axaml`

**Size:** 76 lines
