# `ViewModels/QueryViewModel.cs`

**Purpose:** Query window view-model: tabs, execute/cancel, results, plans, open/save, history, session restore and persist — the guard that asks before F5 runs a script that destroys data, failing closed when the host supplies no confirmation hook, and the Ctrl+Shift+P palette that reads the catalog fresh and hands a chosen script to the caret without running it.

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `class QueryViewModel` : ObservableObject — View-model for the SQL Query window: connection picker, open query tabs, execution (F5 / Ctrl+Enter), cancellation, per-tab result grids, messages, row caps, timing, and tab management.

## Public surface

`SavedConnections`, `Tabs`, `ExplainKeywords`, `ShowKeywordExplainer`, `ShowQueryExplanation`, `CopyToClipboardAsync`, `PickSavePathAsync`, `PickOpenSqlPathAsync`, `PickSaveSqlPathAsync`, `OpenFileCommand`, `SaveFileCommand`, `SaveFileAsCommand`, `OpenHistoryCommand`, `InsertHistoryCommand`, `CopyHistoryCommand`, `ClearHistoryCommand`, `InsertSqlAtCaret`, `History`, `FilteredHistory`, `RecentSqlFiles`, `ConnectCommand`, `NewTabCommand`, `CloseTabCommand`, `CloseOtherTabsCommand`, `CloseAllTabsCommand`, `ExecuteCommand`, `CancelCommand`, `ExecuteSelectionCommand`, `CopyResultsCommand`, `CopyWithHeadersCommand`, `SaveCsvCommand`, `SaveJsonCommand`, `SaveMarkdownCommand`, `SaveInsertScriptCommand`, `FormatSqlCommand`, `ClearResultsCommand`, `ExplainCommand`, `ExplainKeywordCommand`, `TogglePlanCommand`, `ToggleIoTimeCommand`, `EstimatedPlanCommand`, `ShowResultsViewCommand`, `ShowPlanViewCommand`, `OpenPaletteCommand`, `ConfirmDangerousScriptAsync`, `OpenQueryBuilderAction`, `OpenHistoryWindowAction`, `ReplaceActiveTabSql()`, `ShowPaletteAsync`, `RestoreSession()`, `PersistSession()`, `NewTab()`, `OpenSqlFile()`, `SyncRecentList()`, `ApplyHistoryFilter()`

## Referenced by

- `ViewModels/QueryBuilderViewModel.cs`
- `Views/QueryHistoryWindow.axaml.cs`
- `Views/QueryWindow.axaml`
- `Views/QueryWindow.axaml.cs`

**Size:** 1130 lines
