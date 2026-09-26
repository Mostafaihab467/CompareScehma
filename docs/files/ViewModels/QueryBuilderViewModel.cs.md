# `ViewModels/QueryBuilderViewModel.cs`

**Purpose:** Query Constructor view-model: joins, filters, grouping, ordering, regeneration of the SQL on every change.

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `class QueryBuilderViewModel` : ObservableObject — Owns the dialog state for the Query Constructor window.
- `enum ApplyTarget`

## Public surface

`Builder`, `AvailableTables`, `AvailableColumnNames`, `Aggregates`, `OperatorOptions`, `Conjunctions`, `JoinTypeOptions`, `SortDirections`, `SetTable()`, `SetJoinTable()`, `AddPrimaryTable()`, `AddTable()`, `RemoveTable()`, `AddJoin()`, `RemoveJoin()`, `AddFilter()`, `RemoveFilter()`, `AddHaving()`, `RemoveHaving()`, `AddGroupBy()`, `RemoveGroupBy()`, `AddOrderBy()`, `RemoveOrderBy()`, `SelectAllColumns()`, `AddColumn()`, `RemoveColumn()`, `Reset()`, `OnTableChangedAsync()`, `ApplyTarget`

## Referenced by

- `Views/QueryBuilderWindow.axaml`
- `Views/QueryBuilderWindow.axaml.cs`
- `Views/QueryWindow.axaml.cs`

**Size:** 441 lines
