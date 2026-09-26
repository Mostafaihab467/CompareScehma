# CompareSchema — Feature Roadmap & Competitive Strategy

Goal: make the app **compatible with SSMS** for daily tasks and **exclusive** where
competitors (SSMS, DBeaver, DataGrip, Redgate SQL Prompt/Schema Compare) are weak.
Our unique base: **schema compare + SQL query + DB manager + data move** in one
lightweight tool.

Legend: 🏆 exclusive (hard to copy) · ⚡ fast win · 🧭 bigger bet later · ✅ shipped

---

## 🏆 Tier 1 — Exclusive / Killer Features

### 1. Schema-Aware Query Intelligence (beyond SSMS IntelliSense)
We already load schema metadata + FKs (`QuerySchemaService`, `DataMoveService` FK ordering). Extend it:
- ✅ **Visual Query Constructor** (`QueryBuilderWindow` + `SqlBuilder`): pick tables/columns/JOINs/WHERE/GROUP BY/ORDER BY from dropdowns, see the generated T-SQL live, push it into the active query tab or open a new tab and select it. Auto-suggests JOIN ON columns from `sys.foreign_key_columns`.
- ✅ **Auto-JOIN in the editor** (`SqlCompletionProvider.SuggestJoins` + `JoinSuggestionService`): type
  `FROM dbo.[Order] o JOIN dbo.Pre_Users u` and the popup offers `ON o.UserId = u.UserID` — read off the
  key the database enforces, both directions, composite keys whole, one offer per key when a pair has
  two. Silence when there is no key, because an operator does not re-read a suggested ON.
- ✅ **Inline query linting** (`SqlLintService`): unknown tables and qualified/unqualified unknown
  columns are flagged before executing, from the same schema cache IntelliSense uses (SSMS can't).
- ✅ **"Explain my query" panel** (`QueryExplainDialog`, the Explain button): the SELECT parsed into
  which tables it touches, what each clause does and what the aggregates mean. *Not* built: click a
  column in the panel → jump to its definition in the editor.

### 2. Diff-Driven Development workflow
Our core advantage — no competitor combines these:
- ✅ **Schema snapshots as files** (`SchemaSnapshotService` → `.dacpac`): capture a database, then
  compare schema-vs-schema, DB-vs-snapshot, snapshot-vs-snapshot → Git-friendly DB versioning
  without paying for Redgate. The library (`SnapshotLibraryService`) remembers what was captured
  where, and a pairing can be saved under a name (`SavedComparisonsService`).
- ✅ **"What changed since the baseline?"** (`DetectDriftAsync`): diff a live database against a
  captured `.dacpac` and get the three-way answer — added, altered, dropped. *Not* built: capturing
  automatically on connect; a baseline is something the operator takes.
- ✅ **Migration script generator with rollback** (`GenerateScriptBetweenAsync` /
  `GenerateRollbackScriptAsync`): UP and DOWN scripts for any diff, the DOWN previewed, copied and
  saved but never executed by the app.

### 3. Safe Query Guard (exclusive safety angle)
- ✅ **Danger detection before execute** (`QueryGuardService` + `QueryViewModel`): an
  UPDATE/DELETE with no filter that narrows it, `WHERE 1=1`, TRUNCATE, `DROP TABLE`/`DATABASE` or a
  dropped column stops F5 until the operator answers a dialog that names the damage. Reported in
  the Messages pane with a stable rule id. *Not* built: the estimated-affected-rows
  (`SELECT COUNT(*)`) preview and row-level write preview — counting queries production behind the
  operator's back and is its own hazard, and the finding is true whatever the count.
- Pairs with the existing "Allow unsafe changes/drops" toggles
- Marketing angle: *"the SQL editor that protects your data"*

### 4. Query Result → Data-Move pipeline
`MoveDataWindow` already exists: select rows in a result grid → **"Send to Move"** → copy to
another connection/database with FK-order handling. SSMS requires manual scripting; this is one-click ETL.

---

## ⚡ Tier 2 — Fast wins that beat SSMS on UX

| # | Feature | Notes |
|---|---------|-------|
| 5 | **Result grid upgrades** | Excel/CSV/JSON/Markdown copy of selection, instant pivot, per-column filter boxes |
| 6 | **Query history** | Every executed query stored locally, searchable, per connection, with row counts + duration (SSMS history is useless) |
| 7 | **Tab sessions** | Persist open tabs + connections to `%AppData%`, restore on restart |
| 8 | **Multi-connection execution** | Run one query against N servers side-by-side, diff the results (huge for multi-tenant shops) |
| 9 | **Command palette (Ctrl+K)** | Fuzzy-jump to any table/view/proc, generate SELECT/INSERT/DROP script |
| 10 | **Snippets with schema context** | `s sel * from` expands using real table columns; snippets saved to JSON |

---

## 🧭 Tier 3 — Bigger bets (later)

- **11. Performance sidecar**: execution plan + missing-index hints via
  `sys.dm_db_missing_index_details`, explained in plain English
- **12. Data quality inspector**: per-table card — row counts, null %, duplicate key
  candidates, orphaned FK rows
- **13. Documentation export**: auto-generate Markdown/HTML data dictionary from schema
  metadata (column info is already loaded)
- **14. Theming**: dark/modern UI is already in place — keep as the visible advantage over SSMS

---

## Recommended build order

Five of the five shipped; the fifth landed in a different shape from the one this list imagined.

1. **Query history + tab sessions** — done (`Services/QueryHistoryService.cs`,
   `Services/TabSessionService.cs`, the searchable history window)
2. **Auto-JOIN + column validation** — done, in both places a join gets written:
   `Services/QueryBuilderService.cs` proposes the `JOIN … ON` candidates to the Constructor's
   dropdowns, and `Services/JoinSuggestionService.cs` + `SqlCompletionProvider.SuggestJoins` offer
   the same clause in the editor as the JOIN is typed (round 12).
   `Services/SqlLintService.cs` reports unknown tables and unknown columns against the schema cache.
3. **Safe Query Guard** — done (`Services/QueryGuardService.cs`, `Models/QueryGuard.cs`,
   `QueryViewModel.ClearedToRunAsync`): the ad-hoc `UPDATE`/`DELETE`-without-a-`WHERE` case this
   list asked for, plus TRUNCATE / DROP / dropped column, gated before F5 posts anything. The
   compare path keeps its own guards (`AllowUnsafeDrops` / `AllowUnsafeChanges` off by default,
   every script preview-and-confirm, `Services/ClipboardGuard.cs`). What was *not* built is the
   affected-rows preview — see §3.
4. **Result → Move pipeline** — done (`Services/DataMoveService.cs`, the Move Data window,
   results-grid → plan)
5. **Schema snapshots + migration generator** — done (`Services/SchemaSnapshotService.cs`
   `.dacpac` baselines, `Services/SnapshotLibraryService.cs`, `Services/SavedComparisonsService.cs`,
   the UP/DOWN script pair)

## Compatibility checklist (SSMS parity must-haves)

- [x] Multi-tab SQL editor with syntax highlighting (T-SQL)
- [x] Autocomplete / IntelliSense (ranked, fuzzy, context-aware)
- [x] Execute query (F5), selection-only execution, GO batch separators
- [x] Tabular results with cell/column inspection
- [x] Query history (persistent across runs, searchable window), Messages output with
      `Elapsed 0.00s`, cancel of an in-flight query
- [x] Object explorer (server → database → tables/views/procedures/security, right-click
      scripts, object filter, double-click properties)
- [x] Right-click → top-rows read (`SELECT TOP (1000)`), top-rows editable grid (200 rows),
      Script Table as SELECT / INSERT
- [x] Execution plan viewer — estimated plan without executing, actual plan with per-operator
      runtime metrics, diagram, operator warnings and missing-index detail
