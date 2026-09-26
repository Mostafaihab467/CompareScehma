# `Services/QueryGuardService.cs`

**Purpose:** Reads a script's blast radius off the text before it reaches the server: DELETE/UPDATE with no outer filter (a sub-query's WHERE does not count, a CTE's does), WHERE 1=1, TRUNCATE, DROP TABLE/DATABASE, DROP COLUMN as Dangerous; DROP INDEX/PROCEDURE as Advisory. Works on string- and comment-masked text so a commented-out DELETE cannot start a warning.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class QueryGuardService` — The guard that runs a script with your eyes open: what this text will destroy, read off the text itself before the server sees it.

## Public surface

`QueryGuardService`, `Analyze()`

## Referenced by

- `Services/SqlLintService.cs`
- `ViewModels/QueryViewModel.cs`

**Size:** 277 lines
