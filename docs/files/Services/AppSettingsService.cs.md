# `Services/AppSettingsService.cs`

**Purpose:** Loads/saves settings.json and applies UI scale and fonts to Application.Resources.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class AppSettingsService` — Loads/saves display settings to %AppData%/SchemaCompare/settings.json and applies them to Application.Resources so every window updates live via {DynamicResource UiScale} / {DynamicResource CodeFontSize}.

## Public surface

`AppSettingsService`, `Load()`, `Save()`, `ApplyToResources()`

## Referenced by

- `App.axaml.cs`
- `ViewModels/DataCompareViewModel.cs`
- `ViewModels/DiagramViewModel.cs`
- `ViewModels/ImportWizardViewModel.cs`
- `ViewModels/MainViewModel.cs`

**Size:** 72 lines
