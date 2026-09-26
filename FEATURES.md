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
- **Auto-JOIN**: type `FROM Orders o JOIN` → suggest `ON o.CustomerId = c.Id` from real FK metadata
- **Inline query linting**: red squiggle on columns that don't exist in referenced tables *before* executing (SSMS can't)
- **"Explain my query" panel**: parse the SELECT, show which tables/columns it touches — click a column → jump to definition

### 2. Diff-Driven Development workflow
Our core advantage — no competitor combines these:
- **Schema snapshots as files**: save a DB schema to a JSON/DACPAC-like snapshot; compare
  schema-vs-schema, DB-vs-snapshot, snapshot-vs-snapshot → Git-friendly DB versioning
  without paying for Redgate
- **"What changed since yesterday?"**: auto-snapshot on connect, one-click diff of last N snapshots
- **Migration script generator with rollback**: UP and DOWN scripts for any diff

### 3. Safe Query Guard (exclusive safety angle)
- **Danger detection before execute**: UPDATE/DELETE without WHERE, TRUNCATE on a table
  with FKs → inline warning + estimated affected rows (`SELECT COUNT(*)` preview)
- **Statement preview for writes**: show exactly which rows will be affected before committing
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

Four of the five shipped; the fifth was scoped differently from what we ended up with.

1. **Query history + tab sessions** — done (`Services/QueryHistoryService.cs`,
   `Services/TabSessionService.cs`, the searchable history window)
2. **Auto-JOIN + column validation** — done (`Services/QueryBuilderService.cs` proposes the
   `JOIN … ON` candidates from FK metadata; `Services/SqlLintService.cs` reports unknown tables
   and unknown columns against the schema cache)
3. **Safe Query Guard** — *not* built as this list imagined it (an ad-hoc `UPDATE`/`DELETE`
   without a `WHERE` stopped before it runs). What exists instead is the guard on the compare
   path: `AllowUnsafeDrops` / `AllowUnsafeChanges` off by default, every script
   preview-and-confirm, `Services/ClipboardGuard.cs`. The ad-hoc-query guard is still open work.
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
