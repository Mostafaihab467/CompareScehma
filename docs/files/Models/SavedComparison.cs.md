# `Models/SavedComparison.cs`

**Purpose:** One named source/target pairing, each side stored as a reference — a snapshot file, a saved profile's id, or typed server and database — which is why a pairing can never carry a password.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `class SavedComparison` — A source/target pairing the operator ran more than once: the weekly drift check between a live database and a captured baseline, or staging against production.

## Public surface

`SavedComparison`, `Name`, `SourceProfileId`, `SourceSnapshotPath`, `SourceServer`, `SourceDatabase`, `SourceLabel`, `TargetProfileId`, `TargetSnapshotPath`, `TargetServer`, `TargetDatabase`, `TargetLabel`, `AllowUnsafeDrops`, `AllowUnsafeChanges`, `SavedAt`, `DescribeSide()`

## Referenced by

- `Services/SavedComparisonsService.cs`
- `ViewModels/MainViewModel.cs`
- `Views/MainWindow.axaml`

**Size:** 50 lines
