# `Services/FuzzySearch.cs`

**Purpose:** The palette's matcher: one point per matched character, a penalty for any run break that does not land on a word start (so an acronym scores as well as a prefix), a small length penalty, and NoMatch for anything that misses. Stable ordering, case-insensitive, pure.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class FuzzySearch` — Subsequence matcher for the command palette: ranks a short typed query against long dotted names ("dbo.Pre_Users") and prose ("Format document"), and refuses anything whose characters do not all appear in order.

## Public surface

`FuzzySearch`, `Score()`

## Referenced by

- `Models/PaletteItem.cs`
- `ViewModels/CommandPaletteViewModel.cs`

**Size:** 98 lines
