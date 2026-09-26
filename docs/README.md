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
| Query execution, results, plans | `Services/QueryExecutionService.cs`, `Models/QueryResultTable.cs`, `Services/ExecutionPlanService.cs`, `Controls/PlanDiagramControl.cs` |
| Editor behaviour (find, fold, bookmarks, goto, completion, lint) | `Controls/SqlHighlightedEditor.cs` and `Controls/EditorFindBar.cs` / `EditorFolding.cs` / `EditorBookmarks.cs` / `EditorGotoLine.cs` / `SqlCompletionProvider.cs`, plus `Services/SqlLintService.cs` |
| Tabs, session restore, history, recent files | `ViewModels/QueryViewModel.cs`, `Services/TabSessionService.cs`, `QueryHistoryService.cs`, `RecentFilesService.cs` |
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
| Connections, saved profiles, authentication methods, TLS, passwords | `Models/ConnectionInfo.cs`, `Models/AuthMethod.cs`, `Services/SavedConnectionsService.cs`, `Models/SavedConnection.cs` |
| Settings, UI scale, fonts | `Services/AppSettingsService.cs`, `Models/AppSettings.cs`, `Styles/Resources.axaml` |
| Logging, crash reporting, About/diagnostics | `Services/AppLog.cs`, `Program.cs`, `Services/AppInfo.cs`, `Views/AboutWindow.axaml.cs` |
| Clipboard crash workaround | `Services/ClipboardSafety.cs`, `Services/ClipboardGuard.cs` |

## Verification

The app is proven by a headless harness, not by eyeballing: `ReproSsms` in the
scratch working directory drives real windows and dialogs through Avalonia's
headless lifetime and writes PNGs with `RenderTargetBitmap`.

- 793 assertions cover Tier 1, the five Tier 2 rounds and the first Tier 3 round end to
  end, against the local instance (EgyptMart) and a snapshot database of it. Objects the
  designer and the import wizard create are created in `tempdb`, verified on the server and
  dropped again; Query Store is verified against a `__sc19_qs` probe database the run
  creates, enables, works and drops, and the rollback script against a `__sc20_src` /
  `__sc20_snap` pair built to differ in all three ways a schema can differ.
- It performs a live `COPY_ONLY` backup and reads it back; it never restores, never
  `KILL`s, never starts an Agent job and never executes a data-sync script — all four are
  asserted up to the confirmation and declined, and the compare run counts rows on both
  sides before and after to prove nothing was written. The only scripts it approves are
  the Query Store force / unforce pair, and only inside the probe database it owns. A
  rollback (DOWN) script is never executed at all, only previewed, copied and saved.
- Never call `SavedConnectionsService.Save()` from test code, and always
  `AppLog.RedirectDirectory` to a temp probe folder.
- Do not kill a running SchemaCompare instance belonging to the user while testing.

Feature status (done/verified vs not done) lives in
[../PRODUCTION.md](../PRODUCTION.md) — update it in the same change as the code.

## Keeping the docs honest

After adding, renaming or moving source files:

1. add the new path to `PURPOSES` in [purposes.py](purposes.py) (one line, factual),
2. run `python docs/build_docs.py` from the repo root.

Types, public members, named controls and the "Referenced by" lists are extracted
from source, so they cannot drift; only the purpose lines are hand-written.
