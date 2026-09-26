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
1018 assertions, 0 failures — Tier 1, all five Tier 2 rounds, the Tier 3 rounds (rollback,
snapshots, drift, the baseline library, saved comparisons), the round-8 plan/lint fixes and
the round-11 destructive-script guard —
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
`.dacpac` baseline instead of a live database (`46_snapshot_drift.png`), and the same
query through both plan buttons so the estimated diagram and the measured one are
compared side by side (`47_estimated_plan_only.png`, `48_actual_plan_metrics.png`), and the
guard asked to stop an unfiltered `DELETE` in a real query window against a probe database it
seeds, works and drops again (`51_query_guard.png`), and the join suggestion accepted into a real
editor against the keys EgyptMart actually enforces (`52_auto_join.png`). No restore,
no `KILL` and no Agent job is ever executed — all three are asserted up to the
confirmation and declined, and a DOWN script is never executed at all: it is
previewed, toggled against the UP text, copied and saved, and both probe databases
are counted before and after to prove generating it changed nothing. The data
compare never writes either: the same run counts
the source and target rows before and after every compare, script, save and copy, and
requires both counts to be unchanged. Snapshots are read-only too: the capture run counts
the live database's tables and rows before and after capturing, diffing and scripting, and
deletes every `.dacpac` it wrote plus both probe databases before it reports done.
The guard's two approved statements are the only destructive SQL the run ever lets execute,
and only inside `__sc25_guard`, whose table it counts on both sides of every declined
confirmation to prove a stop really stopped.

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
| Snapshot diff stored as a file the operator can keep | **Done, verified** | Round 9 — see the table below |
| The pairing itself saved as a name to re-run | **Done, verified** | Round 10 — see the table below |

## Round 8 — the two plan buttons stopped telling the same story

Reported as a defect: *"what is the difference between the Plan button and the Estimated
button, both give the same result."* There was a difference — one runs the query, one only
compiles it — but the diagram threw away everything the run measured, so a guess and a
measurement drew the same boxes with the same numbers.

Fixing it meant reading a real plan instead of guessing at one. Captured off the local
instance with `SET STATISTICS XML ON`, ShowPlanXML showed the old parser wrong in three places:

- **Runtime metrics are not attributes on `RelOp`.** Every executed operator carries a
  `RunTimeInformation` child with one `RunTimeCountersPerThread` per thread.
  `ExecutionPlanService.ReadRuntimeCounters` reads them: rows, rows-read and page reads
  **sum** across threads (that is the operator's whole work), while elapsed time and
  executions take the **maximum** — the threads ran at the same instant, so adding their
  clocks reports 695 ms for a 355 ms operator. `NumberOfExecutions` does not appear in the
  XML at all; the count is `ActualExecutions`.
- **Row size is `AvgRowSize`, not `EstimateRowSize`.** The parser asked for an attribute the
  server never writes, so `EstimatedRowSize` has been a silent 0 in every plan this app drew.
- **The statement's own clock is a `QueryTimeStats` element** under `QueryPlan`
  (`ElapsedTime`, `CpuTime`), nothing to do with any operator — now `PlanStatement.ActualElapsedMs`
  / `ActualCpuMs`, shown in the statement header's tooltip.

Alongside the corrected parse: `ActualRowsRead` separates an operator that returned 5 rows from
one that examined 65,000 to do it; every box of an executed plan reads `≈ 1K → 48K rows`;
`PlanNode.EstimateSkew` turns that gap into a number and `ExecutionPlan.SkewWarningFactor` (10×)
decides when it becomes a ⚠ on the box — with the two cases that are *not* misses stated
explicitly: an estimate nobody measured, and an operator that legitimately returned nothing
(0 actual rows is an empty result, not a 1000× error). An operator with no counters inside an
executed plan never ran, and its box says `(not run)` rather than standing there with a bare
estimate among measurements. The diagram opens with **ACTUAL plan — rows, time and reads below
were measured while the query ran** or **ESTIMATED plan — the query was compiled, not run:
every number below is the optimizer's guess**, and the status line under the results says the
same thing in the operator's own words (`47_estimated_plan_only.png`, `48_actual_plan_metrics.png`).

The real XML also exposed a regression on the way in: `ExtractPredicate` treated "the first
child that is not `OutputList` or `Warnings`" as the physical-operator element, and
`RunTimeInformation` sits in exactly that position — so predicates and filters disappeared from
actual plans the moment runtime info started being read. It is excluded by name now.

The other half of the round was a lint false alarm: `sys.all_objects`,
`INFORMATION_SCHEMA.ROUTINES` and a three-part `EgyptMart.sys.tables` resolve in every database
and in nobody's schema cache, so `SqlLintService.IsCatalogView` exempts them — while a
misspelled `dbo.Ordrs` is still caught.

| Feature | State | Notes |
|---|---|---|
| 🧭 actual plan ≠ 🌩 estimated plan | **Done, verified** | Same script through both buttons in a live `QueryWindow`: the estimated one has no runtime stats anywhere and says so, the executed one reports measured rows/CPU/elapsed/logical-physical reads per operator, the statement clock, the 48× estimate miss as a ⚠, and boxes that read estimate → actual |
| ShowPlanXML runtime counters | **Done, verified** | `RunTimeInformation` / `RunTimeCountersPerThread` parsed with sum-across-threads for rows and reads, max for time and executions; `AvgRowSize` for row size; `QueryTimeStats` for the statement clock. Harness fixtures use the captured shape, including a two-thread operator that proves the difference between the two |
| Lint leaves catalog views alone | **Done, verified** | `sys.*`, `INFORMATION_SCHEMA.*` and their three-part forms are exempt from the unknown-table rule; real misspellings still flagged |

## Round 9 — the app remembers the baselines it captured

A `.dacpac` made the schema outlive the server. It did not outlive the file dialog: every
later comparison meant finding the folder again and remembering which of twenty
timestamped files was the one from before the deploy. `Services/SnapshotLibraryService.cs`
is that memory — the last twenty baselines, newest first, in a `Recent snapshots` row on
either compare card (`49_snapshot_library.png`).

- **One recording point, three ways in.** `MainViewModel.DescribeSnapshot` is what every
  path passes through — a capture, a Browse, a pick from the list — so it is where
  `Remember` is called, and no route can add a file to a card without adding it to the
  list. Picking a row sets that side to snapshot mode and fills the path; typing a path
  selects the row. Neither duplicates the entry, and the two controls cannot end up
  naming different files.
- **The row is the provenance, not a re-read.** `Models/SnapshotEntry.cs` copies the
  server, database and capture time out of the package once, so drawing twenty rows opens
  no files. `SnapshotInfo.DescribeAge` is shared with the card, so "3 days ago" means the
  same thing in both places, and a package from SSDT or a build pipeline says
  `captured by another tool` rather than being given a capture date it never had.
- **Forgetting is a record operation.** `Forget` drops the row and leaves the `.dacpac`
  exactly where it is — the file is the operator's, and may be the baseline another
  machine still compares against. With nothing picked the button says so instead of
  guessing at a row. A row whose file has gone is dropped when the list loads: it is not a
  choice, it is a dead end.
- **Nothing sensitive is stored.** `snapshot_library.json` holds paths plus what the
  package already said, and the harness reads the file to prove no connection string,
  user name or password can appear in it.

| Feature | State | Notes |
|---|---|---|
| Snapshot library (recent baselines) | **Done, verified** | `Services/SnapshotLibraryService.cs` + `Models/SnapshotEntry.cs`, `MainViewModel.SnapshotLibrary` / `SelectedSourceSnapshot` / `SelectedTargetSnapshot` / `Forget*SnapshotCommand` — newest-first, de-duped by path case-insensitively, capped at 20, persisted beside the other side-stores, pruned of missing files on load |
| Pick a baseline from the compare card | **Done, verified** | `Views/MainWindow.axaml` — a searchable list plus **Forget** on both snapshot cards; picking switches the side to the file, typing the path back selects the row, and a second window opened later reads the same list |

Two harness facts from the round: the library is a real side-store, so the run has to
delete `snapshot_library.json` before the window checks — the offline rows above it live
in the same file, and their counts would otherwise leak into the card's. And re-remembering
the top row returns the *existing* entry rather than a fresh instance, which is what keeps a
bound dropdown from losing its selection mid-interaction; the assertion that proves it has to
compare against the instance the previous `Remember` returned, not the first one.

## Round 10 — a pairing the operator names once and re-runs

Round 9 removed the file hunt. The cards still had to be rebuilt every time — which server,
which database, which baseline, on which side — so the weekly drift check was still a list of
things to remember. `Services/SavedComparisonsService.cs` stores the *pairing* under a name the
operator chose, each side kept as one reference: a `.dacpac` path, the id of a saved profile, or
the server and database typed into the card. One pick in the `Saved comparisons` row fills both
cards, guards included (`50_saved_comparison.png`).

- **A pairing can hold no credential, by construction.** Nothing here is a connection string: a
  side is a file path or a profile's `Id`, and the profile's password stays in the DPAPI-protected
  connection store. A side that authenticates with a user name and has no saved profile is
  *refused at save time* — "…save it as a profile first. A comparison never stores a password." —
  instead of the app inventing a second, weaker place to keep the secret. The harness reads
  `saved_comparisons.json` and shows the profile id and the baseline name in it, and no password
  or connection-string keyword in it, including after a refused save of a card that really had a
  password typed in.
- **Applying names what is missing rather than half-applying.** A pairing whose baseline was moved
  says which file `is no longer there`; one whose profile was deleted says which profile
  `is no longer saved`. Either way the other side is still applied and the row survives — the
  operator named that workflow, and silently comparing something else is the one outcome worse
  than refusing. This is the deliberate opposite of the snapshot library, which prunes a row whose
  file is gone: a file list is a shortcut, a named pairing is something the operator wrote.
- **Alphabetical, replaced by name, and full means full.** Newest-first is right for files and
  wrong for workflows, so the list sorts by name and re-saving a name replaces that row
  case-insensitively. At twenty rows a *new* name is refused with "forget one before saving
  another" rather than evicting a pairing somebody depends on; re-saving a name the list already
  holds is never refused.
- **The data-loss guards travel with the pairing.** `AllowUnsafeDrops` / `AllowUnsafeChanges` are
  saved and restored, because they decide what a script may destroy — re-running a comparison must
  not quietly widen them. Picking a row re-applies the profile through the same `ApplyProfile`
  path the dropdown uses, under the `_applyingProfile` guard, so the selection survives its own
  field writes.

| Feature | State | Notes |
|---|---|---|
| Saved comparisons (source ⇄ target) | **Done, verified** | `Services/SavedComparisonsService.cs` + `Models/SavedComparison.cs`, `MainViewModel.SavedComparisons` / `SelectedSavedComparison` / `ComparisonName` / `SaveComparisonCommand` / `ForgetComparisonCommand` — alphabetical, replaced by name case-insensitively, capped at 20 with a refusal that says so, persisted beside the other side-stores, references only |
| Re-run a pairing from the compare card | **Done, verified** | `Views/MainWindow.axaml` — the `Saved comparisons` card: searchable list with **Forget**, name box with **Save pairing**; one pick rebuilds both sides (profile id → the saved profile, path → the baseline) and the compare runs from there; `Forget` deletes the record and touches no file, no profile and no card |

Three harness facts from the round: replacing a name keeps the *newest* spelling, so the reload
assertion reads `alpha DRIFT` after a re-save, not the `Alpha drift` it started with; the card's
path box and its baseline row sync in *both* directions and the two directions need opposite rules
(see below); and the list is a real side-store like the library, so the run deletes
`saved_comparisons.json` before the window checks instead of inheriting the offline rows.

The middle one took two runs to pin down. `OnSnapshotPathChanged` mirrors the file in the box into
`Selected*Snapshot` — that is what makes a typed path light up its row — and the pick handler used
to begin with "the two already name this file, so return". That early return was load-bearing twice
over: it is also what lets a capture leave its side live while its row is already selected, and one
guard doing two opposite jobs is exactly the thing the next control inherits wrong. Picking and
mirroring are now separate: `OnSelected*SnapshotChanged` always sets file mode for a real pick, and
the path's mirror runs under `_syncingSnapshotRow`, the same trick `_applyingProfile` uses for the
profile dropdown. No miscompare was reachable in the shipped card — the list and the path are hidden
while a side is live, so an operator gets there through the *Compare against a snapshot file* box,
never through a stale row — but both halves are now asserted: a capture stores its path, mirrors its
row and leaves the side live; ticking the box reveals the baseline it captured.

## Round 11 (Tier 4) — the editor answers "what will this destroy?"

Tiers 1 to 3 guarded the compare and deploy path. The query window was the one place an operator
could type `DELETE FROM dbo.Line;`, press F5 and have the app post it without a question — SSMS's
own protection there is "are you sure?", which an operator answers by reflex.
`Services/QueryGuardService.cs` reads the damage off the text before the server sees it, and
`QueryViewModel.ClearedToRunAsync` will not start a run until that damage has been named on screen
and answered (`51_query_guard.png`).

- **The finding is a sentence, not a flag.** The dialog leads with the worst thing in the script —
  *"DELETE FROM dbo.Line has no WHERE, so it removes every row of the table."* — and the same text
  then lands in the Messages pane with its rule id (`Guard [Dangerous] delete-without-where`), so
  the scrollback records what ran unfiltered instead of only that something ran. Rule ids are
  stable for exactly that reason: a test, a log line and a support conversation can name the same
  finding without quoting prose that will be reworded.
- **It claims only what the text can prove, and nothing more.** The guard never connects and never
  counts rows: a `COUNT(*)` preview would query production behind the operator's back, be its own
  long-running hazard on a big table, and change nothing about the decision — "no WHERE" is true at
  one row and at ninety million. So `DELETE`/`UPDATE` with no filter, a `WHERE` made only of
  tautologies, `TRUNCATE`, `DROP TABLE`, `DROP DATABASE` and `ALTER TABLE … DROP COLUMN` are
  Dangerous; the rest of the schema verbs are advice.
- **The filter that counts is the statement's own.** Parenthesised groups are dropped before the
  `WHERE` test, so `UPDATE t SET c = (SELECT … WHERE …)` is still every row of `t` — the inner
  predicate narrows the sub-query, not the write. A `DELETE FROM stale` through
  `;WITH stale AS (SELECT … WHERE …)` *is* credited the CTE's predicate, because that is literally
  the set it deletes. And because T-SQL does not require the semicolon that would make finding a
  statement's end trivial, the splitter cuts at the next verb at bracket depth zero: that is what
  lets `IF EXISTS (…) DELETE FROM t` be judged on the DELETE's own filter, and why `SET` is
  deliberately not a boundary verb — cutting there would flag every filtered
  `UPDATE … SET … WHERE …`, which is the false alarm that gets a guard switched off.
- **A guard that halts routine work is a guard that gets switched off,** so only two levels exist
  and only one of them stops: `DROP INDEX` and `DROP PROCEDURE` are reported and run (Advisory),
  `WHERE 1=1` and a bare `DELETE` do not run until answered. The same is true of the status-bar
  switch: with `GuardAsksBeforeDangerousScripts` off the findings are still written to Messages,
  plus a line saying the run went ahead without a question — put in Messages rather than the
  status line because a run that proceeds overwrites the status with its own outcome, and the note
  is about the run. Turning the guard off is allowed; pretending it never spoke is not.
- **Confirmed, declined and fail-closed all name the same script.** `QueryWindow` installs the hook
  over the shared `ScriptActionDialog`, so the preview, the warning and what runs are one text. With
  no host attached the run refuses itself — *"…nothing here can show you the confirmation — nothing
  ran"* — like `KILL`, the Query Store verbs and the import wizard. The gate sits before
  `IsExecuting`, so a decline leaves the table, the previous results and the messages exactly as
  they were; Ctrl+Enter over a selection is the same script through the same gate; and 🌩 Estimated
  plan is not gated at all, because compiling a `DELETE` under `SHOWPLAN_XML` destroys nothing —
  asserted by counting the probe table's rows on both sides of it.

| Feature | State | Notes |
|---|---|---|
| A script's blast radius read from its text | **Done, verified** | `Services/QueryGuardService.cs` + `Models/QueryGuard.cs` (`GuardLevel` / `GuardFinding` / `GuardReport.Summary` / `.Headline` / `.RequiresConfirmation`) — rules `delete-without-where`, `update-without-where`, `where-filters-nothing`, `truncate-table`, `drop-table`, `drop-database`, `alter-drop-column` as Dangerous, `drop-index` / `drop-<kind>` as Advisory; run over `SqlLintService.StripStringsAndComments` so a `DELETE` in a comment or a string literal cannot start a warning |
| F5 asks before a script that destroys data | **Done, verified** | `QueryViewModel.ClearedToRunAsync` / `ReportToMessages` / `ConfirmDangerousScriptAsync` / `GuardAsksBeforeDangerousScripts`, hook installed in `Views/QueryWindow.axaml.cs` over `ScriptActionDialog`, switch in the status bar of `Views/QueryWindow.axaml` |

Three harness facts from the round. The first version of the splitter treated *any* top-level verb
as a boundary, including the one that opened the statement, so a `DELETE` cut itself short and its
own `WHERE` fell outside it — every filtered delete looked dangerous; the boundary rule needs "the
*next* verb", which is only visible when you read the assertion that failed rather than the one you
expected. Second, that and the infinite span list it turned into (splitting at a verb and then
re-examining the same verb forever, which the harness found as an out-of-memory) were both caught by
running the analyzer over a table of script shapes *before* the 9-minute harness run — the same 29
shapes are now Part 25's offline assertions. Third, the switch-off note was first written to
`tab.StatusMessage` and the harness found it gone: everything after the guard overwrites that line
with the run's own outcome, so a note that has to survive a run belongs in Messages — and the
harness assertion moved with it, to the scrollback.

## Round 12 (Tier 1 §1) — a JOIN answers with the key the database enforces

FEATURES.md §1's last open bullet was Auto-JOIN: *type `FROM Orders o JOIN` and have the editor
suggest `ON o.CustomerId = c.Id` from real FK metadata*. The query constructor already did that from
dropdowns (`QueryBuilderService.GetJoinCandidates`), so the gap was the editor — the place a query
is actually written. `SqlCompletionProvider.SuggestJoins` reads the join the caret is finishing and
`JoinSuggestionService` writes the clause (`52_auto_join.png`).

- **The clause comes from the constraint, never from a name that looks right.** One query on
  connect — `sys.foreign_key_columns` joined to `sys.columns` on both sides — fills
  `SqlCompletionProvider.ForeignKeys`, and every offer is a row of it. That is the whole reason the
  feature is trustworthy: `ON o.UserId = u.UserID` is not a guess about two tables whose names
  rhyme, it is `FK_Order_User`, and the tooltip says so.
- **Restraint is the feature.** A name the metadata doesn't know, a `JOIN` whose table is still
  being typed, an `ON` already written, a `CROSS JOIN`, a pair with no key between them — all give
  an empty list, and the empty list is what keeps the popup credible. An operator accepts a
  suggested `ON` without reading it, because it looks like the schema vouched for it; that only
  holds if the schema really did.
- **The editor does not decide what the operator meant.** Two keys between one pair
  (`Cus_CustomerContact` → `Pre_Users` on both `CustomerID` and `SupplierID`) yield two offers,
  because picking one silently picks the shape of the result set. A key over two columns yields
  *one* clause naming both — half a composite key joins the wrong rows and says nothing wrong while
  doing it — which is why the metadata arrives as one row per column pair and is regrouped by
  constraint name.
- **It is inserted, not typed over.** The word under the caret when a join suggestion appears is the
  table's *alias* — `…JOIN dbo.Pre_Users u|` — and the completion segment the rest of the popup uses
  would replace it, producing `…JOIN dbo.Pre_Users ON o.UserId = u.UserID` with the left side
  pointing at nothing. So a join offer carries its own zero-width segment at the caret and a leading
  space when the caret is against the name, and the harness asserts the resulting document reads as
  a query that runs.
- **A qualified name means its own schema.** `ResolveTableKey` had always thrown the schema away and
  matched on the table name, which is fine for column completion and wrong here: with both
  `dbo.Order` and `sales.Order` in the cache, `FROM sales.[Order] o JOIN dbo.Pre_Users u` was
  offered `dbo`'s key. It now honours an explicit schema and only falls back to name matching when
  the script didn't state one.

| Feature | State | Notes |
|---|---|---|
| Auto-JOIN from real foreign keys | **Done, verified** | `Services/JoinSuggestionService.cs` (`Between` — both directions, composite regrouping, bracketing) + `Models/JoinSuggestion.cs` (`ForeignKeyRef` / `JoinSide` / `JoinSuggestion.Description`), `QuerySchemaService.GetForeignKeysAsync`, `Controls/SqlCompletionProvider.cs` (`ForeignKeys`, `SuggestJoins`, `JoinTailRegex`, the space trigger in `OnTextEntered`) |

Three harness facts from the round. First, the probe caught the schema bug above — 19 of 20 script
shapes matched on the first run and the twentieth was a real defect in code that had been shipping
since the completion list was written, which is what the seconds-cheap probe is for. Second,
`CloseWhenCaretAtBeginning` and the shared replacement segment are both properties of the *window*,
not the item, so an offer that must be inserted rather than typed over has to change them both at
once — setting the segment without disabling that flag leaves a popup that closes itself the instant
it opens, and looks like a feature that never fires. Third, a negative assertion against somebody
else's database is a guess: the live "these two tables share no key" check first named
`Order` ⇄ `ShippingStatus`, which really are related by `FK_Order_ShippingStatus`, so the product
was right and the test failed. That check now asks `sys.foreign_keys` whether the pair is unrelated
before it asks the completion provider, so a schema change reports as *the fixture moved* instead of
as a defect in the feature. (Part 25 also leaves the shared schema cache pointing at its
`__sc25_guard` probe database, which has no keys in it; part 26 connects its own window and waits
for the cache to repopulate rather than trusting what an earlier round loaded.)

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

- The `eta` / `500600` credentials remain in **pushed git history** from before the
  defaults were cleaned; rotating them is an operator action, not a code fix.

## Still open, by name

Four tiers are done and verified, so this is the list of what is *not* there — the things a reader
would otherwise assume the tiers covered:

- **The guard reads the script, not the server.** FEATURES.md §3 also imagined an *estimated
  affected rows* preview and a row-level statement preview before a write commits. Neither is
  built, deliberately: counting rows queries production behind the operator's back and is its own
  hazard on a big table, and the finding the guard states is true whatever the count. Round 11
  shipped the detection half only.
- **A write hidden in a dynamic-SQL string is invisible to the guard.** `EXEC('DELETE FROM t')`
  reads as a string, and strings are masked before analysis precisely so that a commented-out or
  printed `DELETE` cannot start a warning. Accepted, not missed — the alternative is a guard that
  shouts at every literal.
- **Execution-plan graphics for a snapshot**: plans need a live engine, so a card reading from a
  `.dacpac` has no plan to show. Nothing claims otherwise; it is listed here so nobody files it
  as a bug in the snapshot work.
