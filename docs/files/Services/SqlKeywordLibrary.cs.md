# `Services/SqlKeywordLibrary.cs`

**Purpose:** Hand-written teaching content for the keyword explainer dialog.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `record SqlKeywordInfo` (     string Keyword,     string Title,     string Category,     string Explanation,     string Example,     string Tips) — One keyword's teaching content for the ✨ explainer dialog.
- `class SqlKeywordLibrary` — Static, hand-written explanations for the ✨ Explain dropdown — aggregation functions first, then the clauses they pair with.

## Public surface

`SqlKeywordInfo()`, `SqlKeywordLibrary`, `Find()`

## Referenced by

- `Views/AggregationExplainerDialog.axaml.cs`

**Size:** 150 lines
