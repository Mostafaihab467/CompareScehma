# SchemaCompare — module map and where to look

Avalonia 12 / net10.0 desktop app that compares, syncs and manages SQL Server
databases. ~114 source files, roughly 30 k lines. This page is the navigation
layer: read it plus [INDEX.md](INDEX.md) (and the per-file page under
`docs/files/…`) before opening source, and you can answer "where does X live"
without scanning the repo.

## Folder layout

| Folder | Namespace | Contains |
|---|---|---|
| `(root)` | `SchemaCompare` | `Program.cs` (entry, crash logging), `App.axaml(.cs)`, project/solution files, `PRODUCTION.md`, `FEATURES.md` |
| `Views/` | `SchemaCompare.Views` | every `Window` and dialog: `.axaml` layout + `.axaml.cs` behaviour |
| `Controls/` | `SchemaCompare.Controls` | reusable editor/UI controls (`SqlHighlightedEditor`, folding, bookmarks, plan diagram, charts) |
| `ViewModels/` | `SchemaCompare.ViewModels` | one view-model per window, CommunityToolkit.Mvvm |
| `Models/` | `SchemaCompare.Models` | plain data records/classes, no I/O |
| `Services/` | `SchemaCompare.Services` | all SQL Server I/O, pure T-SQL builders, persistence, diagnostics |
| `Styles/` | — | `Resources.axaml` (brushes/spacing), `Styles.axaml` (control styles) |
| `Resources/` | — | `TSQL.xshd` highlighting definition (embedded resource) |
| `Scripts/`, `Tools/` | — | sample load SQL and the PowerShell pressure tester used when verifying health views |
| `docs/` | — | this map, `INDEX.md`, one pointer page per source file, `build_docs.py`, `purposes.py` |

Rules that keep it consistent:

- **Namespace mirrors folder.** A new window goes in `Views/` with
  `x:Class="SchemaCompare.Views.X"` and `namespace SchemaCompare.Views;`.
- The `.csproj` is SDK-style, so new files are picked up automatically — there is
  no file list to edit. Only a new *embedded resource* needs an `ItemGroup` entry.
- Dialogs are `Window` subclasses with a **private constructor** plus
  `static Task<T?> ShowAsync(owner, …)`. That is why the build emits
  `AVLN3001 … no public constructor` warnings for them: expected, not a defect.
- Build with an explicit output dir (a bare `dotnet build` in this folder hits
  MSB1011 because both `.sln` and `.csproj` are present):
  `dotnet build SchemaCompare.csproj -p:OutDir="<scratch>/app/"`.

## How the pieces fit

```
Views/*.axaml.cs  ──hosts──▶  ViewModels/*  ──calls──▶  Services/*  ──▶  SQL Server
        │                          │
   named controls            Func<…>? hooks        Services/ManagerScriptBuilder
   (x:Name in .axaml)        set by the window      (pure strings: preview == run)
```

Three conventions that matter when you change behaviour:

1. **`ManagerScriptBuilder` is pure.** Every statement the user previews is the
   statement that runs; no service builds SQL inline.
2. **VMs fail closed.** A `ShowXxxAsync` / file-picker hook that the host window
   did not set makes the command report "… neither is attached." instead of
   silently doing nothing or running blind.
3. **Destructive paths need an explicit opt-in.** `RESTORE … WITH REPLACE` is
   emitted only when the user ticks it; `KILL` refuses `spid ≤ 50` before even
   connecting.

## Where to look for a feature

| Task | Start here |
|---|---|
| Object Explorer tree, filters, context menus, Script-As | `ViewModels/DbManagerViewModel.cs`, `Services/DbManagerService.cs`, `Models/ManagerTreeModels.cs`, `Views/DbManagerWindow.axaml(.cs)` |
| Safe restore (backup sets, relocation, STOPAT) | `Views/RestoreDatabaseDialog.axaml.cs`, `Models/RestoreModels.cs`, `ManagerScriptBuilder.RestoreDatabase*`, `DbManagerService.ReadBackupSetsAsync` |
| Native backup to .bak | `Views/BackupDatabaseDialog.axaml.cs`, `Services/DatabaseBackupService.cs`, `ManagerScriptBuilder.BackupDatabase/VerifyBackup` |
| Query execution, results, plans | `Services/QueryExecutionService.cs`, `Models/QueryResultTable.cs`, `Services/ExecutionPlanService.cs` (`ReadRuntimeCounters` for the measured numbers), `Models/ExecutionPlanModel.cs`, `Controls/PlanDiagramControl.cs` |
| Editor behaviour (find, fold, bookmarks, goto, completion, lint) | `Controls/SqlHighlightedEditor.cs` and `Controls/EditorFindBar.cs` / `EditorFolding.cs` / `EditorBookmarks.cs` / `EditorGotoLine.cs` / `SqlCompletionProvider.cs`, plus `Services/SqlLintService.cs` |
| Tabs, session restore, history, recent files | `ViewModels/QueryViewModel.cs`, `Services/TabSessionService.cs`, `QueryHistoryService.cs`, `RecentFilesService.cs` |
| Auto-JOIN: the ON clause a JOIN is reaching for | `Services/JoinSuggestionService.cs` (`Between`), `Models/JoinSuggestion.cs` (`ForeignKeyRef`, `JoinSide`), `Controls/SqlCompletionProvider.cs` (`SuggestJoins`, `JoinTailRegex`, `ForeignKeys`, the space trigger in `OnTextEntered`), `Services/QuerySchemaService.cs` (`GetForeignKeysAsync`), `ViewModels/QueryViewModel.cs` (`RefreshSchemaCacheAsync`) |
| "What will this script destroy?" guard | `Services/QueryGuardService.cs` (`Analyze`, `Statements`, `FilterOf` / `CteFilter`), `Models/QueryGuard.cs` (`GuardReport`, `GuardFinding`, `GuardLevel`), `ViewModels/QueryViewModel.cs` (`ClearedToRunAsync`, `ReportToMessages`, `GuardAsksBeforeDangerousScripts`), `Views/QueryWindow.axaml.cs` (`ConfirmDangerousScriptAsync` → `ScriptActionDialog`), `Views/QueryWindow.axaml` (status-bar switch) |
| Ctrl+Shift+P command palette: fuzzy-jump to an object and write its script | `Services/FuzzySearch.cs` (`Score`, `Order`), `Models/PaletteItem.cs`, `Services/CommandCatalogService.cs` (`GetObjectsAsync`, `RowsFor`), `ViewModels/CommandPaletteViewModel.cs`, `Views/CommandPaletteWindow.axaml(.cs)`, `ViewModels/QueryViewModel.cs` (`OpenPaletteAsync`, `PaletteCommandRows`, `ApplyPaletteChoiceAsync`, `ShowPaletteAsync`), `Views/QueryWindow.axaml.cs` (the chord and the palette host) |
| Filtering a result's rows and pivoting them into a new tab | `Services/ResultGridService.cs` (`DistinctValues`, `VisibleRows`, `Pivot`), `Models/ResultGridModels.cs` (`ResultFilter`, `ResultValue`, `ResultAggregates`, `PivotRequest`), `Models/QueryResultTable.cs` (`Filters`, `VisibleRows`, `ApplyFilters`, `FilterNote`, `Summary`), `Views/QueryWindow.axaml.cs` (`BuildResultHeader`, `OpenColumnFilter`, `PaintFunnel`), `Views/ResultPivotDialog.axaml(.cs)`, `ViewModels/QueryViewModel.cs` (`PivotAsync`, `ReportFilter`, `AskPivotAsync`) |
| Health views, blocking chain, KILL, error log | `Services/DbHealthService.cs`, `ViewModels/DbHealthViewModel.cs`, `Models/DbHealthModels.cs`, `Models/HealthActionsModels.cs` |
| Query Store: option state, regressed and top queries, force / unforce a plan | `Views/DbHealthWindow.axaml` (Query Store tab), `ViewModels/DbHealthViewModel.cs` (`LoadQueryStoreAsync`, `ForcePlanAsync`), `Services/DbHealthService.cs` (`GetQueryStoreStateAsync`, `GetRegressedQueriesAsync`, `GetTopQueriesAsync`, `GetQueryPlansAsync`), `Services/ManagerScriptBuilder.cs` (`ForceQueryPlan`, `UnforceQueryPlan`, `EnableQueryStore`) |
| Server-level Object Explorer (databases, logins, Agent jobs, linked servers) and switching the working database | `Models/ServerBrowserModels.cs`, `DbManagerService.GetDatabasesAsync` / `GetServerSecurityAsync` / `GetAgentJobsAsync` / `GetLinkedServersAsync`, `DbManagerViewModel.Make*Folder`, `ManagerScriptBuilder.StartAgentJob` |
| Table / database properties | `Services/DbManagerService.cs` (`GetTablePropertiesAsync`), `Models/TablePropertiesModels.cs`, `Views/TablePropertiesDialog.axaml.cs` |
| Object dependencies (uses / used by) | `Services/DbManagerService.cs` (`GetDependenciesAsync`), `Models/ObjectDependencyModels.cs`, `Views/DependenciesDialog.axaml(.cs)` |
| Row-level data compare and its sync script | `Services/DataCompareService.cs`, `Models/DataCompareModels.cs`, `ViewModels/DataCompareViewModel.cs`, `Views/DataCompareWindow.axaml(.cs)` |
| CSV/TSV import: type inference, column mapping, bulk load | `Services/CsvImportService.cs`, `Models/ImportModels.cs`, `ViewModels/ImportWizardViewModel.cs`, `Views/ImportWizardWindow.axaml(.cs)` |
| New Table / View / Stored Procedure designer | `Models/ObjectDesignerModels.cs`, `Services/ManagerScriptBuilder.cs` (`CreateObject`), `Views/ObjectDesignerDialog.axaml(.cs)`, `DbManagerViewModel.NewObjectCommand` |
| Schema compare / sync / diff colours | `Services/SchemaCompareService.cs`, `Services/LineDiffer.cs`, `Models/SchemaDiffItem.cs` |
| Deployment (UP) and rollback (DOWN) scripts | `Services/SchemaCompareService.cs` (`GenerateScriptBetweenAsync`, `GenerateScriptAsync`, `GenerateRollbackScriptAsync`, `RetargetScriptHeader`), `ViewModels/MainViewModel.cs` (`GenerateRollbackScriptAsync`, `ShowingRollbackScript`, `DisplayedScript`), `Views/MainWindow.axaml` (script pane + `CopyScript_Click` / `SaveScript_Click`) |
| Schema snapshots (.dacpac) and drift against one | `Services/SchemaSnapshotService.cs` (`CaptureAsync`, `ReadSnapshot`, `SuggestedFileName`), `Models/SnapshotInfo.cs`, `Models/SchemaSource.cs`, `ViewModels/MainViewModel.cs` (`CaptureSnapshotAsync`, `BrowseSnapshotAsync`, `GetSource` / `GetTarget`, `SourceCardTitle`), `Views/MainWindow.axaml` (the snapshot checkbox + path row in each card), `Views/MainWindow.axaml.cs` (`PickSnapshotPathAsync`) |
| The list of baselines already captured | `Services/SnapshotLibraryService.cs` (`Remember`, `Find`, `Forget`, `Load`), `Models/SnapshotEntry.cs` (`Display`, `SameProvenanceAs`), `ViewModels/MainViewModel.cs` (`SnapshotLibrary`, `HasSnapshots`, `SelectedSourceSnapshot` / `SelectedTargetSnapshot`, `Forget*SnapshotCommand`, the `Remember` inside `DescribeSnapshot`), `Views/MainWindow.axaml` (the `Recent snapshots` row on each card) |
| A source/target pairing saved under a name | `Services/SavedComparisonsService.cs` (`Save`, `Find`, `Forget`, `Load`), `Models/SavedComparison.cs` (`Display`, `DescribeSide`, `SourceIsSnapshot`), `ViewModels/MainViewModel.cs` (`SavedComparisons`, `SelectedSavedComparison`, `ComparisonName`, `SaveComparison`, `ApplySavedComparison`, `ApplyComparisonSide`, `ComparisonSideProblem`), `Views/MainWindow.axaml` (the `Saved comparisons` card) |
| Connections, saved profiles, authentication methods, TLS, passwords | `Models/ConnectionInfo.cs`, `Models/AuthMethod.cs`, `Services/SavedConnectionsService.cs`, `Models/SavedConnection.cs` |
| Settings, UI scale, fonts | `Services/AppSettingsService.cs`, `Models/AppSettings.cs`, `Styles/Resources.axaml` |
| Logging, crash reporting, About/diagnostics | `Services/AppLog.cs`, `Program.cs`, `Services/AppInfo.cs`, `Views/AboutWindow.axaml.cs` |
| Clipboard crash workaround | `Services/ClipboardSafety.cs`, `Services/ClipboardGuard.cs` |

## Verification

The app is proven by a headless harness, not by eyeballing: `ReproSsms` in the
scratch working directory drives real windows and dialogs through Avalonia's
headless lifetime and writes PNGs with `RenderTargetBitmap`.

- 1158 assertions cover Tier 1, the six Tier 2 rounds and its command palette, the Tier 3 rounds —
  rollback, snapshots,
  drift, the baseline library and saved comparisons — the round-8
  plan and lint fixes, the round-11 destructive-script guard and the round-12 auto-JOIN end to
  end, against the local instance (EgyptMart) and a snapshot database of it. Objects the
  designer and the import wizard create are created in `tempdb`, verified on the server and
  dropped again; Query Store is verified against a `__sc19_qs` probe database the run
  creates, enables, works and drops, the rollback script against a `__sc20_src` /
  `__sc20_snap` pair built to differ in all three ways a schema can differ, and the
  snapshot drift against a `__sc21_live` database captured to a `.dacpac`, then edited in
  those same three ways, and the snapshot library against a `__sc23_snap` capture driven
  through a real compare card (`49_snapshot_library.png`), and a saved `__sc24_cmp` ⇄ baseline
  pairing rebuilt from one pick and compared again (`50_saved_comparison.png`), and the query guard
  against a `__sc25_guard` table it seeds, is refused on, confirms, truncates and counts (`51_query_guard.png`), and the
  auto-JOIN against the keys EgyptMart really enforces plus a composite one built and dropped in
  `tempdb` (`52_auto_join.png`), and the palette through its own chord over the live catalog, with
  a procedure created in `tempdb` mid-run to prove the catalog is never cached
  (`53_command_palette.png`), and the result grid's funnel opened by clicking the glyph in a live
  header, ticked, applied, exported and pivoted into a second result tab over a real money column
  (`54_result_filter_popup.png`, `55_result_filtered.png`, `56_result_pivot.png`)
  — every file the run
  writes is deleted again.
- It performs a live `COPY_ONLY` backup and reads it back; it never restores, never
  `KILL`s, never starts an Agent job and never executes a data-sync script — all four are
  asserted up to the confirmation and declined, and the compare run counts rows on both
  sides before and after to prove nothing was written. The only scripts it approves are
  the Query Store force / unforce pair and the guard's `DELETE` / `TRUNCATE`, and only
  inside a probe database the run creates, works and drops — where the table is counted on
  both sides of every declined confirmation. A
  rollback (DOWN) script is never executed at all, only previewed, copied and saved.
- Never call `SavedConnectionsService.Save()` from test code, and always
  `AppLog.RedirectDirectory` to a temp probe folder. A test that needs a saved profile adds it to
  `MainViewModel.SavedConnections` in memory and takes it back out the same way — the Delete-profile
  *command* persists the store, the collection does not.
- A side-store (`snapshot_library.json`, `saved_comparisons.json`, …) is shared by every service
  instance in the process, so delete its file before the window checks that follow the offline
  ones; otherwise the offline rows leak into the card's counts.
- A real `MainWindow` installs its own file-picker hooks (`PickSnapshotFileAsync`,
  `PickSnapshotSavePathAsync`, …). To test a window's fail-closed "the host gave me no
  picker" path, set the hook to `null` first — under headless Avalonia a real
  `StorageProvider` never answers, and the `await` hangs the whole run.
- Do not kill a running SchemaCompare instance belonging to the user while testing.
- When parsing a server format (ShowPlanXML, a backup header, a DMV column set), capture
  one real sample first — `sqlcmd -S localhost -E -d EgyptMart -y 0 -i q.sql -o out.xml` —
  and build the fixture from it. A fixture written from memory of the format passes offline
  and fails the moment a live query runs against it, which is how round 8 spent a run. The same
  applies to a *negative* claim about somebody else's database: prove the premise from the server
  first (`sys.foreign_keys` said the "unrelated" pair in round 12 was related), so a schema change
  reads as *the fixture moved* rather than as a defect in the feature.
- For pure analyzer code (the query guard's rules, the lint, the formatter), develop against a
  throwaway console probe beside the harness — a table of `(label, input, expectation)` triples
  over a `ProjectReference` to `SchemaCompare.csproj` — and port the triples into the harness part
  afterwards. A full run costs ~9 minutes and stops at its first failure; a probe costs seconds and
  shows every miss at once, which is how round 11 found its statement-boundary bug and an infinite
  loop that the harness only reported as out-of-memory.
- Drive the SQL editor through `ActiveTab.SqlText` and a `Pump()`, never by writing to the
  `TextDocument` behind the window's back: `SyncActiveEditorText` pushes the tab's text into the
  editor it shows, so a document written directly can be overwritten before the capture — and a
  screenshot of the wrong query is how round 12 noticed.
- A read-only `DataGrid` whose rows are `Dictionary<string, object?>` must bind `Mode.OneWay`.
  The column binding is two-way by default and the dictionary's indexer is writable, so rendering a
  cell wrote its display text back into the row: a `decimal` became the `string` `"3900.00"` for the
  rows on screen only, and `SUM` then refused a money column as "not numbers". When a live part
  behaves as if a value's *type* changed, print `value.GetType()` from a throwaway probe before
  suspecting the reader — round 14 found both this and the batch splitter that way in minutes.

Feature status (done/verified vs not done) lives in
[../PRODUCTION.md](../PRODUCTION.md) — update it in the same change as the code.

## Keeping the docs honest

After adding, renaming or moving source files:

1. add the new path to `PURPOSES` in [purposes.py](purposes.py) (one line, factual),
2. run `python docs/build_docs.py` from the repo root.

Types, public members, named controls and the "Referenced by" lists are extracted
from source, so they cannot drift; only the purpose lines are hand-written.
