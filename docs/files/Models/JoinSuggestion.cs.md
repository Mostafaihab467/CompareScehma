# `Models/JoinSuggestion.cs`

**Purpose:** The join metadata as data: one column pair of a foreign key, one table as the script names it (key, alias, text as written) and the ON clause offered from them with the key it came from.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `record struct` ForeignKeyRef(     string Name,     string FromSchema,     string FromTable,     string FromColumn,     string ToSchema,     string ToTable,     string ToColumn,     int Ordinal) — One column pair of one foreign key, exactly as sys.foreign_key_columns reports it: a key over two columns arrives as two rows that share , and pairing them back up is what makes a valid composite ON clause.
- `record struct` JoinSide(string Key, string? Alias, string Written) — One column pair of one foreign key, exactly as sys.foreign_key_columns reports it: a key over two columns arrives as two rows that share , and pairing them back up is what makes a valid composite ON clause.
- `record JoinSuggestion` (string Text, string Description) — An ON clause the editor can offer for the join being typed.

## Public surface

`ForeignKeyRef()`, `JoinSide()`, `JoinSuggestion()`, `ToString()`

## Referenced by

- `Controls/EditorFindBar.cs`
- `Controls/SqlCompletionProvider.cs`
- `Models/QueryGuard.cs`
- `Services/JoinSuggestionService.cs`
- `Services/QueryBuilderService.cs`
- `Services/SavedConnectionsService.cs`
- `Services/SqlFormatter.cs`
- `Services/SqlLintService.cs`

**Size:** 50 lines
