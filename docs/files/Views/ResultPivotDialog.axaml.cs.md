# `Views/ResultPivotDialog.axaml.cs`

**Purpose:** Pivot picker code-behind: the value column is disabled for COUNT, and the preview is built by the same ResultGridService.Pivot the app later uses for the real tab — so what the operator saw is what they got, or the refusal says why.

**Namespace:** `SchemaCompare.Views`

**Code-behind for:** `Views/ResultPivotDialog.axaml`

## Declared types

- `class ResultPivotDialog` : Window — The three questions behind a GROUP BY: which column groups the rows, what each group is reduced to, and which column that reduction reads.

## Public surface

`ShowAsync()`

## Referenced by

- `Models/ResultGridModels.cs`
- `Views/QueryWindow.axaml.cs`
- `Views/ResultPivotDialog.axaml`

**Size:** 131 lines
