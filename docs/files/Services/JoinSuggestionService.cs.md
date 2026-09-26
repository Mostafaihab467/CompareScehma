# `Services/JoinSuggestionService.cs`

**Purpose:** Writes the ON clause a join needs from the keys the database enforces: either direction, a composite key offered whole, one offer per key when a pair has two, brackets only where the name needs them. Pure — it never connects, so the pairing rules hold without a server.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class JoinSuggestionService` — Writes the ON clause a join is about to need, from the foreign-key metadata the editor already holds.

## Public surface

`JoinSuggestionService`, `Between()`

## Referenced by

- `Controls/SqlCompletionProvider.cs`

**Size:** 93 lines
