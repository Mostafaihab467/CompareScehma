# Production readiness ledger

Tracks what SchemaCompare can actually do in production versus what is still
missing. "Verified" below means the headless Avalonia harness exercises it
(`ReproSsms` in the scratch working directory), not that it was eyeballed once.

Last updated: 2026-09-26.

---

## Layout and docs (so nobody re-analyzes the repo)

Windows and dialogs live in `Views/` (namespace `SchemaCompare.Views`);
`Controls/`, `ViewModels/`, `Models/`, `Services/`, `Styles/` keep theirs, so a
file's folder is always its namespace. Start any session with:

- [`AGENTS.md`](AGENTS.md) — build command, conventions, non-negotiable test rules.
- [`docs/README.md`](docs/README.md) — module map plus a "where to look for a
  feature" table.
- [`docs/INDEX.md`](docs/INDEX.md) — all 133 source files, one line each;
  `docs/files/<path>.md` gives that file's purpose, types, public surface,
  named controls and who references it. Regenerate with `python docs/build_docs.py`
  after moving or renaming code (purpose lines live in `docs/purposes.py`).

---

## Tier 1 — first week of daily use

Every Tier 1 row is now done and harness-verified (2026-09-25).

| Feature | State | Where |
|---|---|---|
| Safe restore: backup-set picker, file relocation, STOPAT | **Done, verified** | `Models/RestoreModels.cs`, `ManagerScriptBuilder.RestoreDatabase/RestoreBatches/SuggestedPath`, `DbManagerService.ReadBackupSetsAsync/ReadBackupFilesAsync/GetDefaultFileLocationsAsync/RestoreDatabaseAsync`, `Views/RestoreDatabaseDialog.axaml` |
| Restore never overwrites silently | **Done, verified** | `RestorePlan.Validate` + `RestoreDatabaseAsync` throws unless `ReplaceExisting`; `REPLACE` is only emitted when the user ticks it; the old always-`WITH REPLACE` statement is gone and a harness assertion keeps it from coming back |
| Native `BACKUP DATABASE` / `BACKUP LOG` to .bak | **Done, verified** | `ManagerScriptBuilder.BackupDatabase/VerifyBackup/SuggestedBackupFileName`, `DatabaseBackupService.BackupAsync` (streams the server's own `STATS` messages), `Views/BackupDatabaseDialog.axaml`, menu item + `ManagerNode.CanBackup` in Object Explorer |
| Session / tab restore across restarts | **Done, verified** | `Services/TabSessionService.cs`, `QueryViewModel.RestoreSession/PersistSession` — scratch tabs keep unsaved text, file-backed tabs re-read from disk, a tab whose file vanished is skipped and logged |
| Find & Replace (Ctrl+F / Ctrl+H) | **Done, verified** | `Controls/EditorFindBar.cs` — match case, whole word, navigate, replace-all, undo, read-only |
| Open / save / drag-drop .sql, dirty tab markers | **Done, verified** | `QueryViewModel.OpenSqlFile/SaveSqlFileAsync`, `Services/RecentFilesService.cs` |
| Persistent searchable query history | **Done, verified** | `Services/QueryHistoryService.cs`, `Views/QueryHistoryWindow.axaml` |
| Editor folding | **Done, verified** | `Controls/EditorFolding.cs` — single-pass T-SQL scanner (strings, brackets, comments) for `BEGIN…END`, transactions, `GO` batches, `/*…*/` and `(…)`; Ctrl+M on the caret, FoldAll/UnfoldAll; spans end at the line delimiter so CRLF scripts fold without breaking layout; collapsing never edits the text |
| Editor bookmarks | **Done, verified** | `Controls/EditorBookmarks.cs` — Ctrl+B toggle, Ctrl+K / Ctrl+Shift+K next-previous with wrap, Ctrl+Shift+B clear, glyph margin, marks remap when text above them changes |
| Ctrl+G goto line | **Done, verified** | `Controls/EditorGotoLine.cs` — overlay strip, clamps past the end and says the script's line count, refuses non-numbers, Escape closes |
| `KILL` session from the health view | **Done, verified** | `DbHealthService.KillSessionAsync` (rejects spid ≤ 50 before connecting), `DbHealthViewModel.KillSessionCommand` — fails closed with no confirmation host, states the rollback cost, logs the audit line, refreshes after |
| Blocking chain (head blocker) view | **Done, verified** | `DbHealthService.DescribeBlockingChain/BlockedSessions`, `DbHealthViewModel.ShowBlockingChainCommand` — walks `blocking_session_id` to the head, names direct victims |
| SQL error log viewer | **Done, verified** | `DbHealthService.ReadErrorLogAsync` (`xp_readerrorlog … N'desc'`), Error Log tab in `Views/DbHealthWindow.axaml` — tail size, contains filter, severity-11+ filter, error rows tinted |
| Object Explorer filter (name / schema / type) | **Done, verified** | filter bar in `Views/DbManagerWindow.axaml`, `ExplorerQueryFilter` (`Models/ManagerTreeModels.cs`) + `DbManagerViewModel.ApplyExplorerFilterAsync`, `DbManagerService.GetObjectsAsync` — server-side LIKE, badge says what is applied, clearing restores the full tree |
| Double-click table properties | **Done, verified** | `Views/TablePropertiesDialog.axaml`, `DbManagerService.GetTablePropertiesAsync`, `DbManagerViewModel.DoubleTapNodeCommand` — space, columns, PK, FKs both directions, indexes with usage, triggers, stats, partitioning |

Verified means the headless harness ran it end to end against the local instance:
828 assertions, 0 failures — Tier 1, all five Tier 2 rounds and both Tier 3 rounds —
including a live
`COPY_ONLY` backup of EgyptMart read
back through `RESTORE HEADERONLY` / `FILELISTONLY` (the probe file is deleted
afterwards), the restore and backup dialogs driven field by field
(`33_restore_dialog.png`, `34_backup_dialog.png`, `34b_backup_dialog_rejected.png`),
a restart with the tabs that were open (`35_editor_navigation.png`), the Processes
and Error Log tabs (`36_processes_actions.png`, `36b_error_log.png`), a filtered
tree plus its table properties dialog (`37_table_properties.png`), the server-level
Object Explorer with its sibling-database switch (`39_server_tree.png`), the row-level
data compare against a live copy of the database (`41_data_compare.png`) and the
dependencies dialog on a table with thirteen edges (`40_object_dependencies.png`), and the
Query Store tab against a probe database that really has captured history
(`44_query_store.png`), and the deployment and rollback scripts generated from a
probe pair that differs in all three ways a schema can differ
(`45_rollback_script.png`), and the same three-way drift read back out of a captured
`.dacpac` baseline instead of a live database (`46_snapshot_drift.png`). No restore,
no `KILL` and no Agent job is ever executed — all three are asserted up to the
confirmation and declined, and a DOWN script is never executed at all: it is
previewed, toggled against the UP text, copied and saved, and both probe databases
are counted before and after to prove generating it changed nothing. The data
compare never writes either: the same run counts
the source and target rows before and after every compare, script, save and copy, and
requires both counts to be unchanged. Snapshots are read-only too: the capture run counts
the live database's tables and rows before and after capturing, diffing and scripting, and
deletes every `.dacpac` it wrote plus both probe databases before it reports done.

Seven defects surfaced during that verification and are fixed:
`RESTORE HEADERONLY` has no `Type`/`Description` columns (backup sets showed a blank
type and lost their description — now mapped from `BackupType` / `BackupTypeDescription`);
typing a `STOPAT` date never refreshed the preview or re-enabled OK; renaming the
restore target left the relocation destinations on the source database's own file
names, which would have collided with the running database; `xp_readerrorlog`
rejects `N'reverse'` and empty-string filters on SQL Server 2025, so the log viewer
returned nothing; filtering Objects by name broke every query that has no `WHERE`
clause of its own (Views and Triggers — "Incorrect syntax near 'AND'"), which made a
filter look applied while silently erroring; an operation superseded by a newer
one (typing again mid-tree-load) reported its half-finished failure as an error;
and folding threw on any CRLF script — a fold span ended on the `\n` of a `\r\n`
line, i.e. inside the line delimiter, so AvaloniaEdit aborted the next layout pass
with "produced an element which ends within the line delimiter". Fold ends now stop
at the delimiter, and three harness assertions fold a CRLF document to keep it that way.

## Tier 2 — multi-server and enterprise reality

Round 1 (2026-09-26) is done and harness-verified: Entra ID authentication, the
SQL beautifier and the three extra result exports — screenshot `38_auth_dropdown.png`.
Round 2 (2026-09-26) is done and harness-verified too: the server-level Object
Explorer and SQL Agent jobs — screenshot
`39_server_tree.png`.
Round 3 (2026-09-26) adds the two comparison features a migration needs before anyone
trusts the app with real data: row-level data compare (`41_data_compare.png`) and the
object dependencies view (`40_object_dependencies.png`), both verified offline against
synthetic diffs and again live against EgyptMart and a snapshot database of it that is
deliberately behind the live one, so every number the harness asserts (64 tables against
the snapshot's 56, the three missing `Widgets_FAQ` rows, `Products_Basic`'s 122/117 with
a column the snapshot lacks) was measured with `sqlcmd` first rather than guessed from
the code.

| Feature | State | Notes |
|---|---|---|
| Server-level tree (Databases, Logins, Agent, Linked Servers) | **Done, verified** | `NodeKind.ServerRoot / DatabasesFolder / DatabaseNode / ServerSecurityFolder / Login / ServerRole / AgentFolder / AgentJobsFolder / AgentJob / LinkedServersFolder / LinkedServer`, loaded by `DbManagerService.GetServerOverviewAsync/GetDatabasesAsync/GetServerSecurityAsync/GetLinkedServersAsync` over `ConnectionInfo.ServerConnectionString`-style master connections. The connected database keeps its own node inside Databases, so `DbManagerViewModel.CurrentDatabaseNode` (a tree-wide search) replaced the old top-level lookups. Double-clicking a sibling moves the whole explorer to it via `ConnectionInfo.ForDatabase` and `_activeDatabase` — every command follows, the saved profile is never rewritten, and a sibling is deliberately a leaf so no action can run against a database the window is not pointed at |
| Entra ID / Azure SQL authentication | **Done** | `ConnectionInfo.Authentication` emits `Authentication=…`; one dropdown (`Models/AuthMethod.cs`) offers Windows, SQL and six Entra flows (Default, Interactive, Password, Windows-integrated, app principal, device code). Saved profiles persist it, TLS is forced on for Entra, and `SafeForLog` keeps secrets out of the log |
| SQL Agent jobs: list, last outcome, enable/disable, start | **Done, verified** | `DbManagerService.GetAgentJobsAsync` (msdb `sysjobs` + `syscategories` + the job's own `sysjobhistory` row) under a 🤖 SQL Agent folder; `ManagerScriptBuilder.StartAgentJob/SetAgentJobEnabled` drive `sp_start_job` / `sp_update_job` through the same confirm dialog as every other script, so a declined confirmation runs nothing. An instance with no msdb says "SQL Agent is not available on this instance" instead of throwing |
| Data compare (row-level, key-based) | **Done, verified** | `Services/DataCompareService.cs` lists the tables both sides share, pairs their rows on the literal key and reports what is missing, extra or changed; `Views/DataCompareWindow.axaml` (the Compare Data button on the launcher) writes a review-first INSERT/UPDATE script with `SET XACT_ABORT`/one transaction. Computed columns and row versions are never compared, a table without a primary key is refused, a column the target lacks is dropped from the pairing, differing rows are listed by key (capped), target-only rows are reported unless deletes are opted into, and nothing is ever executed against either database |
| Object dependencies view | **Done, verified** | `DbManagerService.GetDependenciesAsync` reads `sys.sql_expression_dependencies` (with `COL_NAME` for column-level edges), `sys.foreign_keys` in both directions and `sys.triggers`; `Views/DependenciesDialog.axaml` shows the two sides as *This object depends on* / *Depends on this object* with `sys` types translated, and 🕸 View Dependencies is offered on table, view, procedure and function nodes only |
| Import wizard (CSV → table with mapping) | **Done, verified** | `Services/CsvImportService.cs` reads RFC 4180 text (quoted delimiters, doubled quotes, a line break inside a field) with `FileShare.ReadWrite` so an open file is not a failure, and infers a SQL type per column (`int`/`bigint`/`decimal(p,s)`/`date`/`datetime2(3)`/`bit`/`uniqueidentifier`/`nvarchar(n)`, `nvarchar(max)` past the row limit) with nullability from the blanks it saw. `ViewModels/ImportWizardViewModel.cs` + `Views/ImportWizardWindow.axaml` (the 📩 Import CSV button on the launcher) edit that guess — name, type, tick, key — and preview the exact `CREATE TABLE` + load. `LoadAsync` is the only writer: one transaction, `SqlBulkCopy` batches of 5 000, `KeepIdentity` only when an identity column is mapped, computed and unknown columns refused, a bad value named by file line, and a failed load that created the table drops it again. Nothing runs without the script-confirmation hook, and with no host wired the command fails closed |
| Export beyond CSV/TSV (JSON, Markdown, INSERT scripts) | **Done** | `ResultsExportService.ToJson` / `ToMarkdown` / `ToInsertScripts` + 💾 JSON / MD / INSERT buttons. JSON keeps numbers numeric and text unescaped; INSERT refuses to guess a target when the script joins tables |
| SQL beautifier / formatter | **Done** | `Services/SqlFormatter.cs` (Ctrl+Shift+F or ✨ Beautify): keywords upper-cased, one clause per line, blocks indented, literals/brackets/comments untouched, idempotent. `GO` resets the batch indent |
| New Table / View / Stored Procedure designer | **Done, verified** | `Views/ObjectDesignerDialog.axaml` opens from ➕ New Table… / New View… / New Stored Procedure… on the three object folders in the tree (`ManagerNode.CanNewObject`, `DbManagerViewModel.NewObjectCommand`, `Models/ObjectDesignerModels.cs`). A table is built from a column grid (name, type, null, key, identity, default) and a view/procedure from a body editor with a per-kind template; both feed one source of truth, `ManagerScriptBuilder.CreateObject`, so the live preview, the confirmation dialog and the executed script are the same text — the preview shows a one-line refusal instead of invalid DDL. ▶ Execute asks for confirmation and then reloads the folder it created into; 📄 Script to Definition Tab writes nothing and hands the script to the editor for review. The Index and Partition dialogs and Script As → CREATE OR ALTER were already there |
| Query Store: regressed queries, top queries, force / unforce a plan | **Done, verified** | `DbHealthService.GetQueryStoreStateAsync` reads `sys.database_query_store_options` through `ViewColumnsAsync`, so a build missing `wait_stats_capture_mode_desc` still loads instead of throwing; `GetRegressedQueriesAsync` averages each query/plan pair over the first and second half of the window (the `sys.dm_qsn_*` difference views are newer-build only) and keeps pairs slower by the threshold the operator set; `GetTopQueriesAsync` ranks every pair that ran by a whitelisted `ORDER BY`; `GetQueryPlansAsync` returns each plan with `is_forced_plan`, `force_failure_count` and its reason. The Query Store tab in `Views/DbHealthWindow.axaml` has database / window / ranking / "regressed by ≥" pickers, both grids side by side, the selected query's plans and the text as it was captured. All three writes go through `ManagerScriptBuilder` (`ForceQueryPlan`, `UnforceQueryPlan`, `EnableQueryStore`) and the same confirmation host as `KILL`: unforce refuses a plan that is not pinned before it even asks, and with no host attached both commands fail closed. A database with Query Store off says so and states that history from before it was turned on cannot be recovered |

Four defects surfaced while verifying round 2, all of them the kind a live run is
the only way to catch:

- `sys.databases` reports its size as an `int`, and `SqlDataReader.GetInt64` refuses
  one — the exception landed inside the connect path, so the Object Explorer never
  appeared at all. The size is now `CAST … AS bigint` and read defensively.
- `msdb.dbo.sysjobs` has no `category` column (only `category_id`), so the Agent
  listing would have died with "Invalid column name" on the first expand. It joins
  `syscategories` now.
- The local instance is not named `(local)` in `sys.servers` on every build — this
  one reports a machine name. The local row is recognised by `server_id = 0`, which
  is what actually identifies it.
- `ManagerNode.Label` and `Detail` were `init`-only, so a folder could never show the
  count it learned while loading and the server node could never take on its version.
  Both are notifying properties now, which is what lets `Databases (14)` and
  `localhost (17.0.1000.7)` appear without a reconnect.

Round 3 surfaced five more, and two of them were only visible with real data on both
sides of a comparison:

- `sys.index_columns.key_ordinal` is a `tinyint`, so the CLR hands back a `byte` and
  `SqlDataReader.GetInt32` threw `InvalidCastException` — the shared-table list never
  rendered and the failure looked like a connect problem. The discovery query now
  `CAST(pk.key_ordinal AS int)`.
- The `SELECT TOP (rowLimit + 1)` probe row was counted as a compared row, so a scan
  stopped by the row cap reported one more row than it read and could call a truncated
  table identical on the strength of a row it never looked at. Both read loops now stop
  at the cap without incrementing past it.
- The first dependencies query named catalog columns that do not exist
  (`referenced_major_id`, `referenced_minor_name`); the view would have thrown
  "Invalid column name" on the first object asked. It reads `referenced_id` with
  `referenced_minor_id` and turns the latter into a column name through `COL_NAME()`.
- A number compared against text counted as equal whenever both printed the same, so
  `1` and `'1'` were "identical" in the value columns while the key builder refused to
  pair them in the same row. `SameValue` now reports number-against-text as a difference,
  and only compares across numeric types by value (`1` against `1.00` is still no change).
- `DataCompareWindow` handed its save-file and clipboard delegates to the view-model in
  its constructor, which Avalonia runs *before* the owner assigns `DataContext` — so
  Save script… and Copy script silently did nothing. The assignment moved to a
  `DataContextChanged` handler, and the harness asserts both hooks are non-null once the
  window is up.

Round 4 (2026-09-26) is the round that writes to a database: the CSV import wizard and
the New Table / View / Stored Procedure designer, screenshotted as `42_import_wizard.png`
and `43_new_object_designer.png`. Both were verified twice over — offline against a
fixture whose fields carry a comma, a doubled quote, a line break and blanks, and again
live against `tempdb`, where the harness measures the row counts and column types before
and after (`42_import_wizard.png` shows four records loaded into an existing table with
three of seven columns matched), then drops every probe object and asserts none is left
behind. The designer's created table, view and procedure are checked on the server
(`is_identity`, `is_primary_key` with `type = 1`) and the Tables / Views folders list
them without a manual refresh.

Six more defects, most of them only a live run could show:

- `LoadAsync` rebuilt the `CREATE TABLE` itself and dropped the key columns on the way,
  so the table the operator confirmed and the table that ran were different scripts —
  the ticked primary key simply was not there. The key list now travels with the
  request and the harness asserts the confirmed text is the executed text.
- Text values were trimmed before being stored, so `"  Kim  "` arrived as `"Kim"` and a
  field of spaces arrived as `NULL`. Conversion now strips spaces only where they
  cannot be data (numbers, dates, GUIDs, flags); a text column gets the file's own
  bytes, and a blank means blank.
- `SanitizeName` trimmed leading as well as trailing underscores, which renamed the
  harness's own `__sc17_orders` probe table to `sc17_orders`. Leading underscores and a
  `#` temp prefix are part of a name.
- The generated `CREATE TABLE` had a comma after its last column: the builder appended
  `line + ","` per column and then did `sb.Length -= 2` to eat it back, which on a CRLF
  `StringBuilder` eats `\r\n` and leaves the comma. The column list is joined explicitly
  now, and the harness asserts no `,\s*\)` reaches the server.
- `ManagerScriptBuilder.CreateObject` refuses an identity on a text column, a column
  listed twice, a column with no type and an unnamed object — the server's "column
  names are not unique" / "IDENTITY can be defined only on int types" arrive as a
  one-line reason in the preview instead of a red error dialog after the confirm.
- The tree did not notice what it had just created. `ReloadFolderAsync` bailed out on
  `folder.Loader == null`, and the Tables / Views / Procedures folders are filled
  eagerly by the database loader so none of them has a `Loader`: the object existed on
  the server, the identity column and clustered PK were verified in `sys.columns` and
  `sys.indexes`, and the folder still listed the old set — the same no-op hit the manual
  Refresh verb on those folders. `ReloadFolderAsync` now falls back to
  `ReloadObjectFolderAsync`, which re-queries the catalogue under the current explorer
  filter and swaps the folder's children in place.

Round 5 (2026-09-26) is Query Store, screenshotted as `44_query_store.png`. It is the
first round that needs a server with history, so the harness makes its own: it creates
`__sc19_qs` with the app's own `EnableQueryStore` script, shortens the statistics interval
to a minute, runs the same query 25 times, flushes with `sp_query_store_flush_db`, and
drops the database at the end of the run (the pool is cleared first, because a disposed
connection still holds its session and the server will not drop a database that is in
use). EgyptMart — which has never had Query Store on — is asserted to report `OFF` with
its grids explained rather than blank, and its own option is asserted unchanged after the
whole run. The force round trip is measured on the server and not in the grid:
`sys.query_store_plan.is_forced_plan` goes 0 → 1 after the approved script and 1 → 0 after
unforce, and the text the confirmation dialog showed is the text that ran.

Five more defects, all of them found by running against a real Query Store:

- `sys.query_store_runtime_stats.execution_type_desc` is `Regular` on this build; `Normal`
  was the pre-2017 spelling. Filtering `= N'Normal'` matched nothing, so both reports said
  "nothing slow here" about a database with 25 executions in the window. The filter is
  `IN (N'Regular', N'Normal')` now.
- Every Query Store time column is `datetimeoffset` holding UTC, and SQL Server reads a
  plain `datetime2` parameter in the *session's* zone. On this UTC+3 instance the window's
  upper bound landed three hours in the past and quietly excluded every recent interval —
  the same empty-grid lie as the filter above. Bounds are `DateTimeOffset(utc, +00:00)`
  parameters now (`DbHealthService.UtcBound`).
- `sys.sp_query_store_unforce_plan` on this build demands `@plan_id` as well as
  `@query_id` and answers the single-argument form with Msg 313.
- `ALTER DATABASE … SET QUERY_STORE = ON` rejects a bare `STALE_QUERY_THRESHOLD_DAYS` (it
  belongs inside `CLEANUP_POLICY = (…)`) and refuses `DATA_FLUSH_INTERVAL_SECONDS` below
  60 — so the enable script carries `CLEANUP_POLICY`, and the probe tunes the statistics
  interval instead of the flush interval.
- `HasQueryStorePlans` was only re-raised on the "store is off" path, so after a reload the
  plan pane kept a stale `true` and its "select a query above" hint never appeared over an
  empty grid. The notification moved to the load's `finally`.

## Tier 3 — roadmap differentiators (FEATURES.md §2)

Round 6 (2026-09-26) is the first Tier 3 round: the rollback script, screenshotted as
`45_rollback_script.png`. The harness builds a pair of probe databases that differ in each
of the three ways a schema can differ — `__sc20_src` has a table, a view and a procedure the
other lacks, `__sc20_snap` has a table the first lacks, and the shared `Audit.note` column is
`NVARCHAR (200)` on one side and `(100)` on the other — and then asserts the two scripts
against each other rather than against a transcription: statement for statement, the UP's
`CREATE TABLE [dbo].[Bespoke]` is the DOWN's `DROP TABLE [dbo].[Bespoke]`, the UP's
`DROP TABLE [dbo].[Legacy]` is the DOWN's `CREATE TABLE`, and the column goes back to the
width the *target* had. Neither probe database is ever executed against: both are counted
before and after and asserted unchanged, then dropped.

Three defects came out of generating that reversal:

- DacFx writes the database it modelled into `:setvar DatabaseName`, `:setvar
  DefaultFilePrefix` and the "Deployment script for …" comment, and `GenerateScript(name)`
  only rewrites the `USE` statement. Read forwards the two agree by accident; reversed, the
  body named the source while the `USE` named the target — a script that prints one database
  and defaults another. Every script now passes through
  `SchemaCompareService.RetargetScriptHeader`.
- The deploy's "allow unsafe drops" gate cannot be inherited by the rollback: reversing an
  addition *is* a drop, so a DOWN script that honoured the guard would undo nothing.
  `GenerateRollbackScriptAsync` takes no drop flag, drops on purpose, and says so in its own
  header; the data-loss guard still carries through (the guarded text is 2,729 chars against
  the unguarded 2,008, because DacFx wraps the narrowing `ALTER COLUMN` in a loss check).
- `GenerateScript` refuses a comparison that found nothing — "Performing script generation is
  not possible for this comparison result" — which the UP path had always surfaced as a red
  error dialog for two databases that legitimately match. An empty diff now returns a
  two-line script saying there is nothing to deploy or undo.

Round 7 (2026-09-26) added the snapshots, screenshotted as `46_snapshot_drift.png`. The
harness captures `__sc21_live` to a 3,803-byte package, then edits the live database in all
three ways at once — drops a view, adds a table, widens a column — and reads the drift
straight back out of the file: `Deleted:Table, Changed:Table, Added:View`, with the revert
naming all three kinds. It then proves what a snapshot is *not*: `__sc21_other` carries the
identical schema and five hundred rows against the package's three, and the compare finds
zero differences, because no row ever entered the file.

Seven things came out of building it:

- A `.dacpac` keeps almost nothing about its author: `DacPackage.Load` exposes only `Name`,
  `Description`, `Version` and the pre/post scripts. So the capture writes
  `SchemaCompare snapshot from {server} at {O-format UTC}` into the description and
  `SnapshotInfo` parses it back out — provenance travels inside the file, and a snapshot
  copied to another machine still says where and when it came from.
- DAC reports the same progress line up to 120 times for a two-object database, because it
  messages per object *and* per phase. `CaptureAsync` de-duplicates case-insensitively: the
  capture above shows 89 distinct lines.
- A script whose wanted state is a file cannot say "run this" the way a live-vs-live script
  can: anything the live database gained since the baseline appears below as a `DROP`. Those
  scripts now open with a `-- NOTE:` header naming the file and warning exactly that, and
  `ApplyChangesAsync` refuses a snapshot target outright rather than opening a connection
  that has nowhere to write.
- Retargeting had to be asserted on the *body*: the new `-- NOTE:` line legitimately contains
  the captured database's name, so scanning the whole script for it proved nothing. The check
  now starts at the `/*` where DacFx's own text begins, and confirms
  `:setvar DatabaseName "__sc21_other"` for a `__sc21_live` package.
- Capturing does **not** flip the side that was captured to file mode. Taking a target's
  baseline immediately before deploying to it is the common case, and switching the card to
  "read from this file" would break the very workflow the capture was for; the path is stored
  and the card title says which mode it is in.
- Two harness facts worth repeating: `MainWindow` installs its own picker hooks, so a
  fail-closed "no picker available" test has to take the hook away first — under headless
  Avalonia a real `StorageProvider` never answers, and the run hung for twenty minutes at
  42 s CPU doing exactly that. And a window-driven capture is named from the clock, so the
  fake picker writes into its own subfolder; otherwise a run that crosses no minute boundary
  overwrites the baseline the drift checks depend on.
- A card that reads from a file has no business offering a **saved server profile**: the
  screenshot showed exactly that, a `Saved profile` dropdown above a snapshot path. Both
  cards now hide the profile row with the rest of the live fields.

| Feature | State | Notes |
|---|---|---|
| Migration script generator (UP) | **Done, verified** | `SchemaCompareService.GenerateScriptAsync` → `GenerateScriptBetweenAsync` / `ApplyChangesAsync` with per-object exclusion and FK-safe batch reordering |
| Rollback / DOWN script generation | **Done, verified** | `SchemaCompareService.GenerateRollbackScriptAsync` — the same diff with the endpoints swapped, headed at the deployed database. `Rollback` button in the compare toolbar, one script pane that toggles between UP and DOWN (`MainViewModel.ShowingRollbackScript`), Copy and Save always hand out whichever is shown. Preview and file only: the app never executes a DOWN script |
| Schema snapshots as files, snapshot-vs-snapshot | **Done, verified** | `Services/SchemaSnapshotService.cs` writes a live database to a `.dacpac` (schema only, permissions ignored, extraction verified) and reads one back; `Models/SchemaSource.cs` makes *either* side of a comparison a database or a file, so live-vs-live, snapshot-vs-live and file-vs-file all run through the one `CompareAsync` / `GenerateScriptBetweenAsync` path. `Capture snapshot…` on a live card, `Compare against a snapshot file` + Browse on either |
| Drift detection ("what changed since yesterday") | **Done, verified** | The same compare, pointed at a baseline: capture once, and every later run against that file answers what the database lost (`Added` — only the baseline has it), gained (`Deleted`) and widened or narrowed (`Changed`). The file carries its own provenance, so the card states `Captured from localhost/DB at … UTC` and `4 h ago` from the package, not from a sidecar the operator can lose |
| Snapshot diff stored as a file the operator can keep | **Not done** | a capture is a file today; there is no "list my snapshots" view, and nothing remembers the last one used |

## Already production-grade

Security and data-safety from the hardening pass (verified by the same harness):
credentials are no longer baked into defaults; `AllowUnsafeDrops` /
`AllowUnsafeChanges` default to false and gate DacFx `BlockOnPossibleDataLoss`;
script confirmations fail closed when the host hook is missing; destructive grid
edits confirm; saved profiles store DPAPI-protected passwords only; per-connection
`EncryptConnection` / `TrustServerCertificate`; audit labels never carry a password.

Observability: rotating `AppLog` that surfaces its own write failures, assembly
version metadata, and an About/diagnostics window with dependency versions, build
diagnostics and the log tail.

Health dashboard already covers server info, CPU ring buffer, memory, schedulers,
perf counters, processes, database list, wait stats, expensive queries, deadlocks,
file IO and missing indexes — do not rebuild these as "Activity Monitor".

## Known product defects not in the tiers above

- 🧭 Actual plan and 🌩 Estimated plan diagrams look identical: the plan parser
  reads only optimizer estimates and discloses `ActualRows` / `ActualCpu` /
  `ActualLogicalReads`.
- SQL lint flags catalog views such as `sys.all_objects` as unknown tables.
- The `eta` / `500600` credentials remain in **pushed git history** from before the
  defaults were cleaned; rotating them is an operator action, not a code fix.
