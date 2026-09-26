# `Services/QueryBuilderService.cs`

**Purpose:** Extras for the Constructor: FK-based join suggestions and per-table column lists.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class QueryBuilderService` — Optional enrichments for the Query Constructor: best-effort FK column pairs (used to pre-fill the JOIN ON clause), and per-table column lists (used to populate the column dropdowns after the user picks tables).
- `record JoinSuggestion` (string LeftSchema, string LeftTable, string LeftColumn,                                         string RightSchema, string RightTable, string RightColumn)

## Public surface

`QueryBuilderService`, `JoinSuggestion()`, `GetJoinSuggestionsAsync()`, `PopulateColumns()`

## Referenced by

- `ViewModels/QueryBuilderViewModel.cs`

**Size:** 84 lines
