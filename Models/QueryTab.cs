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

    /// <summary>.sql file this tab was loaded from or last saved to; null when never saved.</summary>
    [ObservableProperty] private string? _filePath;

    /// <summary>Editor text changed since the last open/save — shown as * in the tab header.</summary>
    [ObservableProperty] private bool _isDirty;

    partial void OnSqlTextChanged(string value)
    {
        if (!IsReloadingFromFile) IsDirty = true;
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(Header));

    /// <summary>Set while the view-model pushes file contents in, so a load does not look like an edit.</summary>
    public bool IsReloadingFromFile { get; set; }

    /// <summary>Marks the tab as matching <paramref name="path"/> on disk.</summary>
    public void MarkSavedAt(string path)
    {
        FilePath = path;
        Title = Path.GetFileName(path);
        IsDirty = false;
    }

    /// <summary>Actual execution plan captured with the last run (🧭 Plan toggle on).</summary>
    [ObservableProperty] private ExecutionPlan? _plan;
    public bool HasPlan => Plan != null;

    /// <summary>Results-pane view: false = grids, true = plan diagram.</summary>
    [ObservableProperty] private bool _showPlanView;

    public bool ShowResultsGrid => HasResults && !ShowPlanView;
    public bool ShowEmptyState => !HasResults && !ShowPlanView;
    public bool ShowPlanDiagram => HasPlan && ShowPlanView;

    partial void OnPlanChanged(ExecutionPlan? value) => OnPropertyChanged(nameof(HasPlan));
    partial void OnShowPlanViewChanged(bool value) => RaiseViewFlags();
    partial void OnHasResultsChanged(bool value) => RaiseViewFlags();

    private void RaiseViewFlags()
    {
        OnPropertyChanged(nameof(ShowResultsGrid));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowPlanDiagram));
    }

    public ObservableCollection<QueryResultTable> Results { get; } = [];
    public ObservableCollection<string> Messages { get; } = [];

    /// <summary>Cancellation for the in-flight execution, owned by the view-model.</summary>
    internal CancellationTokenSource? ExecutionCts { get; set; }

    public string Header => (IsExecuting ? "▶ " : "") + Title + (IsDirty ? " *" : "");

    partial void OnIsExecutingChanged(bool value) => OnPropertyChanged(nameof(Header));
    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(Header));
}
