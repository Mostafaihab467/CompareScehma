# `Models/QueryBuilderModel.cs`

**Purpose:** Query Constructor state: aggregate functions, filter operators, join options.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `enum AggregateFunction` — State for the visual Query Constructor.
- `class AggregateFunctionExtensions` — T-SQL name for an aggregate, e.g.
- `enum FilterOperator`
- `record FilterOperatorOption` (FilterOperator Operator, string Label)
- `record JoinTypeOption` (JoinType JoinType, string Label)
- `class FilterOperatorExtensions`
- `enum FilterConjunction`
- `enum JoinType`
- `class JoinTypeExtensions`
- `enum SortDirection`
- `class SelectedColumn` : ObservableObject — One selected output column (or "all columns").
- `class BuilderTable` : ObservableObject — A table participating in the FROM clause, with its own alias.
- `class JoinClause` : ObservableObject — One JOIN clause joining a second (or later) table onto the FROM list.
- `class FilterCondition` : ObservableObject — One WHERE predicate; conjunction with the previous one is AND/OR.
- `class HavingCondition` : ObservableObject — One HAVING predicate over an aggregated group: e.g.
- `class OrderByItem` : ObservableObject — One ORDER BY entry.
- `class GroupByItem` : ObservableObject — One GROUP BY entry (alias + column picked separately).
- `class QueryBuilderModel` : ObservableObject — Root model.

## Public surface

`AggregateFunction`, `AggregateFunctionExtensions`, `ToSql()`, `FilterOperator`, `FilterOperatorOption()`, `ToString()`, `JoinTypeOption()`, `FilterOperatorExtensions`, `ToFriendlyLabel()`, `FilterConjunction`, `JoinType`, `JoinTypeExtensions`, `SortDirection`, `Table`, `Columns`, `SelectedColumns`, `Tables`, `Joins`, `Filters`, `Having`, `GroupBy`, `OrderBy`, `Reset()`

## Referenced by

- `Services/QueryBuilderService.cs`
- `Services/SqlBuilder.cs`
- `ViewModels/QueryBuilderViewModel.cs`
- `Views/CreatePartitionDialog.axaml.cs`
- `Views/ImportWizardWindow.axaml`
- `Views/QueryBuilderWindow.axaml`
- `Views/QueryBuilderWindow.axaml.cs`

**Size:** 291 lines
