# `Services/SavedComparisonsService.cs`

**Purpose:** The pairings the operator names and re-runs: alphabetical, replaced by name, capped — and stored as references (a profile id, a snapshot path), so a pairing can never hold a credential and a missing reference is named on apply instead of pruned.

**Namespace:** `SchemaCompare.Services`

## Declared types

- `class SavedComparisonsService` — The pairings the operator compares over and over — a live database against a captured baseline, staging against production — so a weekly drift check is one pick instead of rebuilding both cards from memory.

## Public surface

`SavedComparisonsService`, `Entries`, `Load()`, `Find()`, `Save()`, `Forget()`

## Referenced by

- `ViewModels/MainViewModel.cs`

**Size:** 98 lines
