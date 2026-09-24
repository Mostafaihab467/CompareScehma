using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

/// <summary>
/// One open query tab: editor text, execution state and its result tables.
/// </summary>
public partial class QueryTab : ObservableObject
{
    [ObservableProperty] private string _title = "Query 1";
    [ObservableProperty] private string _sqlText = "SELECT TOP 100 *\r\nFROM dbo.TableName;\r\n";
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private string _elapsedText = string.Empty;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _hasMessages;

    /// <summary>Active result table selected in the results pane.</summary>
    [ObservableProperty] private QueryResultTable? _selectedResult;

    /// <summary>Cell selected in the active result grid: "Column: value".</summary>
    [ObservableProperty] private string _selectedCellText = "No cell selected.";

    [ObservableProperty] private string _lintSummary = string.Empty;
    public bool HasLintIssues => !string.IsNullOrEmpty(LintSummary);
    partial void OnLintSummaryChanged(string value) => OnPropertyChanged(nameof(HasLintIssues));

    public ObservableCollection<QueryResultTable> Results { get; } = [];
    public ObservableCollection<string> Messages { get; } = [];

    /// <summary>Cancellation for the in-flight execution, owned by the view-model.</summary>
    internal CancellationTokenSource? ExecutionCts { get; set; }

    public string Header => IsExecuting ? $"▶ {Title}" : Title;

    partial void OnIsExecutingChanged(bool value) => OnPropertyChanged(nameof(Header));
    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(Header));
}
