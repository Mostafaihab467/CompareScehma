# `ViewModels/QueryViewModel.cs`

**Purpose:** Query window view-model: tabs, execute/cancel, results, plans, open/save, history, session restore and persist.

**Namespace:** `SchemaCompare.ViewModels`

## Declared types

- `class QueryViewModel` : ObservableObject — View-model for the SQL Query window: connection picker, open query tabs, execution (F5 / Ctrl+Enter), cancellation, per-tab result grids, messages, row caps, timing, and tab management.

## Public surface

`SavedConnections`, `Tabs`, `ExplainKeywords`, `ShowKeywordExplainer`, `ShowQueryExplanation`, `CopyToClipboardAsync`, `PickSavePathAsync`, `PickOpenSqlPathAsync`, `PickSaveSqlPathAsync`, `OpenFileCommand`, `SaveFileCommand`, `SaveFileAsCommand`, `OpenHistoryCommand`, `InsertHistoryCommand`, `CopyHistoryCommand`, `ClearHistoryCommand`, `InsertSqlAtCaret`, `History`, `FilteredHistory`, `RecentSqlFiles`, `ConnectCommand`, `NewTabCommand`, `CloseTabCommand`, `CloseOtherTabsCommand`, `CloseAllTabsCommand`, `ExecuteCommand`, `CancelCommand`, `ExecuteSelectionCommand`, `CopyResultsCommand`, `CopyWithHeadersCommand`, `SaveCsvCommand`, `SaveJsonCommand`, `SaveMarkdownCommand`, `SaveInsertScriptCommand`, `FormatSqlCommand`, `ClearResultsCommand`, `ExplainCommand`, `ExplainKeywordCommand`, `TogglePlanCommand`, `ToggleIoTimeCommand`, `EstimatedPlanCommand`, `ShowResultsViewCommand`, `ShowPlanViewCommand`, `OpenQueryBuilderAction`, `OpenHistoryWindowAction`, `ReplaceActiveTabSql()`, `RestoreSession()`, `PersistSession()`, `NewTab()`, `OpenSqlFile()`, `SyncRecentList()`, `ApplyHistoryFilter()`

## Referenced by

- `ViewModels/QueryBuilderViewModel.cs`
- `Views/QueryHistoryWindow.axaml.cs`
- `Views/QueryWindow.axaml`
- `Views/QueryWindow.axaml.cs`

**Size:** 951 lines
