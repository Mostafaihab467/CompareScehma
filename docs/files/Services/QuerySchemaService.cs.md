# `Services/QuerySchemaService.cs`

**Purpose:** Schema cache used by IntelliSense: tables, views, columns and the foreign keys a join suggestion is read from, loaded lazily per connection.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class QuerySchemaService` — Schema metadata for IntelliSense-style completion: table/view names, column names per table, and lazily-loaded on connect.
- `record TableInfo` (string Schema, string Name)

## Public surface

`QuerySchemaService`, `TableInfo()`, `GetTablesAsync()`, `GetColumnsByTableAsync()`, `GetForeignKeysAsync()`

## Referenced by

- `Controls/SqlCompletionProvider.cs`
- `Models/QueryBuilderModel.cs`
- `Services/DataMoveService.cs`
- `Services/QueryBuilderService.cs`
- `Services/SqlLintService.cs`
- `ViewModels/QueryBuilderViewModel.cs`
- `ViewModels/QueryViewModel.cs`
- `Views/QueryBuilderWindow.axaml.cs`

**Size:** 112 lines
