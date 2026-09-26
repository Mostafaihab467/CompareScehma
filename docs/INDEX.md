# Source index

One page per source file: purpose, declared types, public surface and who references it. Read a page instead of opening the file whenever you only need to know where something lives. Module map and how-to-find-it table: [README](README.md). Feature status: [../PRODUCTION.md](../PRODUCTION.md).

## (root)

- [`App.axaml`](files/App.axaml.md) — Application markup: merges the app styles, and the Fluent/DataGrid/AvaloniaEdit themes.
- [`App.axaml.cs`](files/App.axaml.cs.md) — Avalonia Application: applies saved display settings on startup and opens the startup window.
- [`Program.cs`](files/Program.cs.md) — STAThread entry point: crash logging for unhandled exceptions, then the Avalonia app builder.

## Views

- [`Views/AboutWindow.axaml`](files/Views/AboutWindow.axaml.md) — Layout of the About/diagnostics dialog.
- [`Views/AboutWindow.axaml.cs`](files/Views/AboutWindow.axaml.cs.md) — About code-behind: version and environment facts, log tail, copy-to-diagnostics text.
- [`Views/AggregationExplainerDialog.axaml`](files/Views/AggregationExplainerDialog.axaml.md) — Layout of the keyword explainer dialog.
- [`Views/AggregationExplainerDialog.axaml.cs`](files/Views/AggregationExplainerDialog.axaml.cs.md) — Keyword explainer: hand-written meaning, example T-SQL and tips for one SQL keyword.
- [`Views/BackupDatabaseDialog.axaml`](files/Views/BackupDatabaseDialog.axaml.md) — Layout of the native BACKUP DATABASE/LOG dialog.
- [`Views/BackupDatabaseDialog.axaml.cs`](files/Views/BackupDatabaseDialog.axaml.cs.md) — Backup dialog logic (BackupDraft): server-side default path, backup type, APPEND vs FORMAT, VERIFY ONLY, preview and OK gating.
- [`Views/BackupWindow.axaml`](files/Views/BackupWindow.axaml.md) — Layout of the BACPAC export window (DacFx), separate from the native .bak backup dialog.
- [`Views/BackupWindow.axaml.cs`](files/Views/BackupWindow.axaml.cs.md) — BACPAC export code-behind: picks a database, runs DatabaseBackupService, reports progress.
- [`Views/CreatePartitionDialog.axaml`](files/Views/CreatePartitionDialog.axaml.md) — Layout of the Create Partition dialog.
- [`Views/CreatePartitionDialog.axaml.cs`](files/Views/CreatePartitionDialog.axaml.cs.md) — Create Partition dialog: collects a PartitionSpec and returns the partition function/scheme plus split script.
- [`Views/DataCompareWindow.axaml`](files/Views/DataCompareWindow.axaml.md) — Layout of the data compare window: two connections, table list, differing rows, sync script.
- [`Views/DataCompareWindow.axaml.cs`](files/Views/DataCompareWindow.axaml.cs.md) — Data compare code-behind: supplies the save-file and clipboard hooks the view-model needs once its DataContext arrives.
- [`Views/DbHealthWindow.axaml`](files/Views/DbHealthWindow.axaml.md) — Layout of the DB Health window: CPU/waits/processes/missing-indexes/query-store/error-log tabs, Activity Monitor style.
- [`Views/DbHealthWindow.axaml.cs`](files/Views/DbHealthWindow.axaml.cs.md) — DB Health code-behind: refresh timer, the clipboard and script-confirmation hosts every write verb (KILL, Query Store force/unforce/enable) needs, and the window-scoped shutdown.
- [`Views/DbManagerWindow.axaml`](files/Views/DbManagerWindow.axaml.md) — Layout of Object Explorer: server/database tree, filter bar, and the Data/Structure/Definition/Execute tabs.
- [`Views/DbManagerWindow.axaml.cs`](files/Views/DbManagerWindow.axaml.cs.md) — Object Explorer code-behind: tree context menus, Script-As, dialog hosts (restore, backup, properties, script confirm), filter UI wiring.
- [`Views/DbPropertiesDialog.axaml`](files/Views/DbPropertiesDialog.axaml.md) — Layout of the read-only database properties view.
- [`Views/DbPropertiesDialog.axaml.cs`](files/Views/DbPropertiesDialog.axaml.cs.md) — Database properties dialog: renders the options/summary DbManagerService reads for one database.
- [`Views/DependenciesDialog.axaml`](files/Views/DependenciesDialog.axaml.md) — Layout of the read-only object dependencies dialog: uses and used-by grids with an empty state.
- [`Views/DependenciesDialog.axaml.cs`](files/Views/DependenciesDialog.axaml.cs.md) — Dependencies dialog: static ShowAsync host for one ObjectDependencies graph.
- [`Views/DiagramWindow.axaml`](files/Views/DiagramWindow.axaml.md) — Layout of the database diagram window: toolbar, search, canvas.
- [`Views/DiagramWindow.axaml.cs`](files/Views/DiagramWindow.axaml.cs.md) — Diagram window code-behind: zoom (incl. Ctrl+wheel), pan, drag, save/load of the layout.
- [`Views/ImportWizardWindow.axaml`](files/Views/ImportWizardWindow.axaml.md) — Import wizard layout: file and destination cards, editable column mapping grid, script preview pane.
- [`Views/ImportWizardWindow.axaml.cs`](files/Views/ImportWizardWindow.axaml.cs.md) — Supplies the file picker and the script-confirmation dialog to the import view-model once its DataContext arrives.
- [`Views/MainWindow.axaml`](files/Views/MainWindow.axaml.md) — Layout of the launcher window: source and target cards that are each a live database or a snapshot file, compare, the one script pane that toggles UP/DOWN, buttons into every other window.
- [`Views/MainWindow.axaml.cs`](files/Views/MainWindow.axaml.cs.md) — Launcher window code-behind: opens saved connections, copies and saves whichever script the pane shows, the snapshot file pickers both directions, and every other window from the menu.
- [`Views/MoveDataWindow.axaml`](files/Views/MoveDataWindow.axaml.md) — Layout of the data-move wizard: source/target picker, table list, plan preview.
- [`Views/MoveDataWindow.axaml.cs`](files/Views/MoveDataWindow.axaml.cs.md) — Data-move code-behind: builds the DataMovePlan, confirms, and runs DataMoveService with progress.
- [`Views/NewIndexDialog.axaml`](files/Views/NewIndexDialog.axaml.md) — Layout of the New Index dialog.
- [`Views/NewIndexDialog.axaml.cs`](files/Views/NewIndexDialog.axaml.cs.md) — New Index dialog: collects an IndexSpec and hands the generated CREATE INDEX script to the caller.
- [`Views/ObjectDesignerDialog.axaml`](files/Views/ObjectDesignerDialog.axaml.md) — New Table / View / Stored Procedure dialog: kind, schema and name, a column grid for tables or a T-SQL body editor for views and procedures, over a live script preview.
- [`Views/ObjectDesignerDialog.axaml.cs`](files/Views/ObjectDesignerDialog.axaml.cs.md) — Drives the object designer: swaps the column grid for the body editor by kind, keeps the preview equal to the statement that would run, and refuses to close on a definition the builder rejects.
- [`Views/QueryBuilderWindow.axaml`](files/Views/QueryBuilderWindow.axaml.md) — Layout of the visual Query Constructor dialog.
- [`Views/QueryBuilderWindow.axaml.cs`](files/Views/QueryBuilderWindow.axaml.cs.md) — Constructor dialog code-behind: modal host for QueryBuilderViewModel, applies generated SQL back to the calling tab.
- [`Views/QueryExplainDialog.axaml`](files/Views/QueryExplainDialog.axaml.md) — Layout of the plain-English explain-query dialog.
- [`Views/QueryExplainDialog.axaml.cs`](files/Views/QueryExplainDialog.axaml.cs.md) — Explain dialog: headline, step-by-step breakdown and deterministic safety warnings for the selected SQL.
- [`Views/QueryHistoryWindow.axaml`](files/Views/QueryHistoryWindow.axaml.md) — Layout of the query history window: search box and entry list.
- [`Views/QueryHistoryWindow.axaml.cs`](files/Views/QueryHistoryWindow.axaml.cs.md) — History window code-behind: filters persisted history and copies or reopens a chosen script.
- [`Views/QueryWindow.axaml`](files/Views/QueryWindow.axaml.md) — Layout of the query window: tab strip, SQL editor, results grid, messages, plan panes.
- [`Views/QueryWindow.axaml.cs`](files/Views/QueryWindow.axaml.cs.md) — Query window code-behind: shortcuts (F5/Ctrl+L/Ctrl+F/Ctrl+M/Ctrl+B/Ctrl+G), open/save .sql, drag-drop, clipboard guard, session restore on close.
- [`Views/RestoreDatabaseDialog.axaml`](files/Views/RestoreDatabaseDialog.axaml.md) — Layout of the safe restore dialog: media path, backup set picker, relocation grid, options, script preview.
- [`Views/RestoreDatabaseDialog.axaml.cs`](files/Views/RestoreDatabaseDialog.axaml.cs.md) — Restore dialog logic (RestoreDraft, RestoreFileRow): STOPAT validation, relocation re-suggestions, REPLACE gating, warnings that block OK.
- [`Views/ScriptActionDialog.axaml`](files/Views/ScriptActionDialog.axaml.md) — Layout of the run-or-copy script confirmation dialog.
- [`Views/ScriptActionDialog.axaml.cs`](files/Views/ScriptActionDialog.axaml.cs.md) — Script confirmation dialog: shows the exact T-SQL about to run, returns true only when the user approves execution.
- [`Views/TablePropertiesDialog.axaml`](files/Views/TablePropertiesDialog.axaml.md) — Layout of the read-only table properties view.
- [`Views/TablePropertiesDialog.axaml.cs`](files/Views/TablePropertiesDialog.axaml.cs.md) — Table properties dialog: renders a TableProperties snapshot (space, columns, keys, indexes, triggers, stats, partitions).

## Controls

- [`Controls/DiagramTableCard.axaml`](files/Controls/DiagramTableCard.axaml.md) — Layout of one draggable table card in the diagram canvas.
- [`Controls/DiagramTableCard.axaml.cs`](files/Controls/DiagramTableCard.axaml.cs.md) — Diagram table card: columns, PK/FK markers, selection and drag behaviour.
- [`Controls/EditorBookmarks.cs`](files/Controls/EditorBookmarks.cs.md) — SSMS-style bookmarks: Ctrl+B toggle, Ctrl+K / Ctrl+Shift+K navigate, glyph margin, marks remap on edits.
- [`Controls/EditorFindBar.cs`](files/Controls/EditorFindBar.cs.md) — Find & replace strip (Ctrl+F / Ctrl+H) built in code, with match highlighting and replace-all undo.
- [`Controls/EditorFolding.cs`](files/Controls/EditorFolding.cs.md) — T-SQL folding: single-pass scanner for BEGIN/END, transactions, batches, comments, parens, plus FoldingManager glue.
- [`Controls/EditorGotoLine.cs`](files/Controls/EditorGotoLine.cs.md) — Ctrl+G go-to-line overlay: clamps past the end, rejects non-numbers, Escape to close.
- [`Controls/MiniChart.cs`](files/Controls/MiniChart.cs.md) — Sparkline chart control used by the DB Health graphs (CPU, waits).
- [`Controls/PlanDiagramControl.cs`](files/Controls/PlanDiagramControl.cs.md) — Execution-plan diagram: left-to-right operator boxes with per-operator icons, details panel and Ctrl+wheel zoom.
- [`Controls/SearchableComboBox.cs`](files/Controls/SearchableComboBox.cs.md) — ComboBox with a search box above the list, used by every dropdown that can hold many items.
- [`Controls/SqlCompletionProvider.cs`](files/Controls/SqlCompletionProvider.cs.md) — IntelliSense-style completion data for the editor: keywords, schema objects and column names.
- [`Controls/SqlHighlightedEditor.cs`](files/Controls/SqlHighlightedEditor.cs.md) — Shared T-SQL editor control: highlighting, lint squiggles, diff line shading, find/fold/bookmark/goto wiring.

## ViewModels

- [`ViewModels/DataCompareViewModel.cs`](files/ViewModels/DataCompareViewModel.cs.md) — Data compare view-model: two connections, table discovery, the row-level comparison run, and the save/copy of the script it never executes.
- [`ViewModels/DbHealthViewModel.cs`](files/ViewModels/DbHealthViewModel.cs.md) — DB Health view-model: polling, samples, blocking chain, KILL, error log tail and the Query Store tab (state banner, regressed and top queries, plan list, force/unforce behind the confirmation host).
- [`ViewModels/DbManagerViewModel.cs`](files/ViewModels/DbManagerViewModel.cs.md) — Object Explorer view-model: server and database tree loading, filters, scripting, database switching, restore/backup, Agent job and new-object designer flows, all fail-closed when a dialog host is missing. ReloadFolderAsync re-queries a folder — including the eagerly-filled object folders, which have no Loader — so the tree shows what a create or drop just did to the server.
- [`ViewModels/DiagramViewModel.cs`](files/ViewModels/DiagramViewModel.cs.md) — Diagram view-model: schema load, table search, layout, persistence.
- [`ViewModels/ImportWizardViewModel.cs`](files/ViewModels/ImportWizardViewModel.cs.md) — View-model of the import wizard: read file, review the mapping, match an existing table, then load — failing closed without a confirmation host.
- [`ViewModels/MainViewModel.cs`](files/ViewModels/MainViewModel.cs.md) — Launcher view-model: the two comparison sides (live database or snapshot file, each capturable), the difference list, and both directions of the deployment script — UP preview and DOWN rollback, one of them shown at a time.
- [`ViewModels/QueryBuilderViewModel.cs`](files/ViewModels/QueryBuilderViewModel.cs.md) — Query Constructor view-model: joins, filters, grouping, ordering, regeneration of the SQL on every change.
- [`ViewModels/QueryViewModel.cs`](files/ViewModels/QueryViewModel.cs.md) — Query window view-model: tabs, execute/cancel, results, plans, open/save, history, session restore and persist.

## Models

- [`Models/AppSettings.cs`](files/Models/AppSettings.cs.md) — Persisted display preferences: UI zoom and editor font size.
- [`Models/AuthMethod.cs`](files/Models/AuthMethod.cs.md) — The authentication dropdown: Windows, SQL and the Entra ID flows, each mapped to the SqlClient Authentication= value it emits.
- [`Models/BackupModels.cs`](files/Models/BackupModels.cs.md) — Backup request model for the native BACKUP statement.
- [`Models/CompareResultSummary.cs`](files/Models/CompareResultSummary.cs.md) — Aggregate counts for a schema-compare run.
- [`Models/ConnectionInfo.cs`](files/Models/ConnectionInfo.cs.md) — Connection parameters (server, DB, Windows/SQL/Entra auth, TLS), the connection strings they build, a log-safe label and the sibling-database clone the explorer switches to.
- [`Models/DataCompareModels.cs`](files/Models/DataCompareModels.cs.md) — Data compare records: the per-table entry with its discovered key and shared columns, one differing row, and a table's diff with its insert/update/delete statements.
- [`Models/DataMoveTable.cs`](files/Models/DataMoveTable.cs.md) — Data-move model: eligible tables, plan, relation order and per-table result.
- [`Models/DbHealthModels.cs`](files/Models/DbHealthModels.cs.md) — Health rows from DMVs: CPU samples, processes, waits, counters, missing indexes, deadlocks, plus QueryStoreState (option banner text) and the regressed/top/plan rows.
- [`Models/DbObjectInfo.cs`](files/Models/DbObjectInfo.cs.md) — Object list row for the tree (schema, name, DbObjectType).
- [`Models/DiagramModels.cs`](files/Models/DiagramModels.cs.md) — Diagram state: table nodes, columns, relations and the persisted layout.
- [`Models/ExecutionPlanModel.cs`](files/Models/ExecutionPlanModel.cs.md) — Parsed execution plan: operators, statements, missing-index suggestions.
- [`Models/HealthActionsModels.cs`](files/Models/HealthActionsModels.cs.md) — Rows for the health actions: error log record and one blocking-chain link.
- [`Models/ImportModels.cs`](files/Models/ImportModels.cs.md) — ImportColumn (one file column and where it maps), TargetColumn, CsvTable and ImportResult for the import wizard.
- [`Models/ManagerTreeModels.cs`](files/Models/ManagerTreeModels.cs.md) — Object Explorer node model: NodeKind for both the database and the server scope, ManagerNode with its lazy loader and context-menu flags, ExplorerQueryFilter and the index/key metadata records.
- [`Models/ObjectDependencyModels.cs`](files/Models/ObjectDependencyModels.cs.md) — Object dependency graph: one Uses/Used-by edge with its sys type_desc translated for a DBA, and the whole set of neighbours around one catalog object.
- [`Models/ObjectDesignerModels.cs`](files/Models/ObjectDesignerModels.cs.md) — DesignerKind (table, view, procedure), one DesignerColumn per table column and the DesignerSpec the designer hands back with its Execute-now choice.
- [`Models/QueryBuilderModel.cs`](files/Models/QueryBuilderModel.cs.md) — Query Constructor state: aggregate functions, filter operators, join options.
- [`Models/QueryHistoryEntry.cs`](files/Models/QueryHistoryEntry.cs.md) — One persisted history entry: script, timing, server, database, outcome.
- [`Models/QueryResultTable.cs`](files/Models/QueryResultTable.cs.md) — One result set or affected-rows summary produced by a batch.
- [`Models/QueryTab.cs`](files/Models/QueryTab.cs.md) — One open query tab: text, file path, dirty flag, results, execution state.
- [`Models/RestoreModels.cs`](files/Models/RestoreModels.cs.md) — Restore domain model: BackupSetInfo, BackupFileInfo, RestorePlan and its validation rules.
- [`Models/SavedConnection.cs`](files/Models/SavedConnection.cs.md) — A saved connection profile: its authentication method, how its password is held and how it becomes a ConnectionInfo.
- [`Models/SchemaDiffItem.cs`](files/Models/SchemaDiffItem.cs.md) — One schema-compare diff line and its Added/Removed/Modified status.
- [`Models/SchemaSource.cs`](files/Models/SchemaSource.cs.md) — One side of a schema comparison as either a live database or a snapshot file, so compare, script and deploy never assume a reachable server.
- [`Models/ServerBrowserModels.cs`](files/Models/ServerBrowserModels.cs.md) — Rows behind the server-scope tree nodes: instance overview, databases with state and size, logins and server roles, linked servers, Agent jobs.
- [`Models/SnapshotInfo.cs`](files/Models/SnapshotInfo.cs.md) — What a .dacpac snapshot says about itself: the database and server it was captured from, when, and how old that makes it.
- [`Models/StoredProcParam.cs`](files/Models/StoredProcParam.cs.md) — One stored-procedure parameter and the value typed on the Execute tab.
- [`Models/TableColumn.cs`](files/Models/TableColumn.cs.md) — Column metadata with human-readable type, nullability and defaults.
- [`Models/TableFilter.cs`](files/Models/TableFilter.cs.md) — One WHERE-clause filter row used by the grid data tab.
- [`Models/TablePropertiesModels.cs`](files/Models/TablePropertiesModels.cs.md) — Read-only per-table properties snapshot: space, columns, PK, FKs both ways, checks, indexes, triggers, stats, partitions.

## Services

- [`Services/AppInfo.cs`](files/Services/AppInfo.cs.md) — Version and environment facts shown in About and in diagnostics text.
- [`Services/AppLog.cs`](files/Services/AppLog.cs.md) — Rotating plain-text log; AppLog.RedirectDirectory is how the harness keeps probes out of the real log.
- [`Services/AppSettingsService.cs`](files/Services/AppSettingsService.cs.md) — Loads/saves settings.json and applies UI scale and fonts to Application.Resources.
- [`Services/ClipboardGuard.cs`](files/Services/ClipboardGuard.cs.md) — Routes an editor's Ctrl+C/X/V through ClipboardSafety so a locked clipboard cannot kill the app.
- [`Services/ClipboardSafety.cs`](files/Services/ClipboardSafety.cs.md) — Never-throwing clipboard calls; the Avalonia cut/copy/paste crash workaround.
- [`Services/CsvImportService.cs`](files/Services/CsvImportService.cs.md) — Reads a delimited file (RFC 4180 quoting), infers SQL types from a sample, builds the CREATE TABLE and bulk-loads rows in one transaction.
- [`Services/DataCompareService.cs`](files/Services/DataCompareService.cs.md) — Row-level key comparison: table discovery with shared keys, the streamed row diff with its literal key pairing, value equality by type, and the review-first insert/update/delete script it writes without ever executing SQL against the target.
- [`Services/DataMoveService.cs`](files/Services/DataMoveService.cs.md) — Row synchronization between two databases in safe dependency order.
- [`Services/DatabaseBackupService.cs`](files/Services/DatabaseBackupService.cs.md) — Native BACKUP DATABASE/LOG plus VERIFYONLY, and the older DacFx BACPAC export; its SQL71562 detection is shared with the snapshot capture.
- [`Services/DbHealthService.cs`](files/Services/DbHealthService.cs.md) — DMV reads for health, blocking chain walk, KILL, the xp_readerrorlog tail, and the Query Store reads (version-tolerant options, regressed halves, ranked top queries, plans per query).
- [`Services/DbManagerService.cs`](files/Services/DbManagerService.cs.md) — Data access for Object Explorer: object and server-level browsing (databases, logins, linked servers, Agent jobs), object scripting, restore media reads, table/database properties.
- [`Services/DiagramAutoLayoutService.cs`](files/Services/DiagramAutoLayoutService.cs.md) — Layered auto-layout: parents above children.
- [`Services/DiagramPersistenceService.cs`](files/Services/DiagramPersistenceService.cs.md) — Saves and loads diagram layouts as JSON.
- [`Services/DiagramSchemaService.cs`](files/Services/DiagramSchemaService.cs.md) — Reads tables, columns and foreign keys of one database for the diagram.
- [`Services/ExecutionPlanService.cs`](files/Services/ExecutionPlanService.cs.md) — Parses ShowPlanXML into the plan model, for both estimated and actual plans.
- [`Services/LineDiffer.cs`](files/Services/LineDiffer.cs.md) — Line-level LCS diff used to colour source-only and target-only lines.
- [`Services/ManagerScriptBuilder.cs`](files/Services/ManagerScriptBuilder.cs.md) — Pure T-SQL builders: script-as, index/partition DDL, CreateObject for the designer, restore batches, backup/verify, SQL Agent job calls and the Query Store force/unforce/enable scripts — preview equals execution, and an invalid definition or id throws a one-line reason instead of emitting broken DDL.
- [`Services/QueryBuilderService.cs`](files/Services/QueryBuilderService.cs.md) — Extras for the Constructor: FK-based join suggestions and per-table column lists.
- [`Services/QueryExecutionService.cs`](files/Services/QueryExecutionService.cs.md) — Executes ad-hoc batches: GO splitting, cancellation, messages, result streaming, statistics and plan capture.
- [`Services/QueryHistoryService.cs`](files/Services/QueryHistoryService.cs.md) — Persistent, capped query history store in the data folder.
- [`Services/QuerySchemaService.cs`](files/Services/QuerySchemaService.cs.md) — Schema cache used by IntelliSense: tables, views, columns, loaded lazily per connection.
- [`Services/RecentFilesService.cs`](files/Services/RecentFilesService.cs.md) — Most-recently-opened .sql paths, stored without file contents.
- [`Services/ResultsExportService.cs`](files/Services/ResultsExportService.cs.md) — Renders results as TSV, CSV, JSON, Markdown or INSERT scripts, and works out the INSERT target from the script.
- [`Services/SavedConnectionsService.cs`](files/Services/SavedConnectionsService.cs.md) — Reads and writes saved connection profiles, including DPAPI-protected passwords.
- [`Services/SchemaCompareService.cs`](files/Services/SchemaCompareService.cs.md) — DacFx schema compare over any pair of live databases or snapshot files, and the one comparison path behind both script directions — forward deploy, reversed rollback — with the function-before-view reorder, the deploy-database retargeting and the note a file on either side forces on the script.
- [`Services/SchemaSnapshotService.cs`](files/Services/SchemaSnapshotService.cs.md) — Schema snapshots as .dacpac files: schema-only DAC extract with the SQL71562 verify-off retry, the capture provenance written into and read back out of the package, and the UTC-stamped file name that keeps two captures of one day apart.
- [`Services/SqlBuilder.cs`](files/Services/SqlBuilder.cs.md) — Pure T-SQL generator for the visual Query Constructor.
- [`Services/SqlExplainerService.cs`](files/Services/SqlExplainerService.cs.md) — Deterministic plain-English analysis of a script: steps, joins, effects and safety warnings.
- [`Services/SqlFormatter.cs`](files/Services/SqlFormatter.cs.md) — T-SQL beautifier behind Ctrl+Shift+F: tokenizes first, so only case, whitespace and line breaks change.
- [`Services/SqlKeywordLibrary.cs`](files/Services/SqlKeywordLibrary.cs.md) — Hand-written teaching content for the keyword explainer dialog.
- [`Services/SqlLintService.cs`](files/Services/SqlLintService.cs.md) — Editor diagnostics: unmatched delimiters, typos, unknown tables, unqualified columns.
- [`Services/TabSessionService.cs`](files/Services/TabSessionService.cs.md) — Saves and restores the open query tabs across runs.
- [`Services/TsqlHighlighting.cs`](files/Services/TsqlHighlighting.cs.md) — Loads the bundled TSQL.xshd into the editor's highlighting definitions.

## Styles

- [`Styles/Resources.axaml`](files/Styles/Resources.axaml.md) — Shared brushes, spacing and typography resources referenced across all windows.
- [`Styles/Styles.axaml`](files/Styles/Styles.axaml.md) — App-wide control styles (buttons, tree, tabs, grids, dialogs).

## Resources

- [`Resources/TSQL.xshd`](files/Resources/TSQL.xshd.md) — T-SQL syntax highlighting definition embedded into the assembly.

## Scripts

- [`Scripts/EgyptMart_HealthMonitor_Load.sql`](files/Scripts/EgyptMart_HealthMonitor_Load.sql.md) — Sample load script that creates activity in EgyptMart so the health views have something to show.

## Tools

- [`Tools/DBPressureTest.ps1`](files/Tools/DBPressureTest.ps1.md) — PowerShell load generator used to exercise blocking, waits and CPU sampling.

_136 pages. Regenerate after any code change with `python docs/build_docs.py`._
