using Avalonia.Controls;
using Avalonia.Interactivity;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.Views;

/// <summary>
/// The three questions behind a <c>GROUP BY</c>: which column groups the rows, what each group
/// is reduced to, and which column that reduction reads. The preview is the real pivot over the
/// rows on screen — the same call the view-model makes when this dialog closes — so a refusal
/// (a text column asked for SUM) shows here rather than after the operator commits.
/// </summary>
public partial class ResultPivotDialog : Window
{
    private const int PreviewGroups = 8;

    private readonly QueryResultTable _table;
    private readonly string[] _columns;

    public ResultPivotDialog(QueryResultTable table)
    {
        InitializeComponent();
        _table = table;
        _columns = table.Columns.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();

        HeaderText.Text = $"Pivot — {table.Title}";
        ScopeText.Text = table.HasFilters
            ? $"{table.VisibleRowCount:N0} of {table.RowCount:N0} row(s) on screen will be grouped · {table.FilterNote}"
            : $"{table.VisibleRowCount:N0} row(s) on screen will be grouped. Nothing is sent to the server.";

        foreach (var column in _columns)
        {
            GroupBox.Items.Add(column);
            ValueBox.Items.Add(column);
        }
        foreach (var kind in ResultAggregates.All)
            KindBox.Items.Add(kind.Label());

        GroupBox.SelectedIndex = 0;
        KindBox.SelectedIndex = 0;                      // COUNT: works on any column
        var firstNumber = Array.FindIndex(_columns, IsNumericColumn);
        ValueBox.SelectedIndex = firstNumber < 0 ? 0 : firstNumber;

        KindBox.SelectionChanged += (_, _) => { UpdateValueState(); UpdatePreview(); };
        GroupBox.SelectionChanged += (_, _) => UpdatePreview();
        ValueBox.SelectionChanged += (_, _) => UpdatePreview();

        CancelBtn.Click += (_, _) => Close(null);
        AddBtn.Click += (_, _) => Close(BuildRequest());

        UpdateValueState();
        UpdatePreview();
    }

    public static Task<PivotRequest?> ShowAsync(Window owner, QueryResultTable table) =>
        new ResultPivotDialog(table).ShowDialog<PivotRequest?>(owner);

    private ResultAggregateKind SelectedKind =>
        KindBox.SelectedIndex >= 0 && KindBox.SelectedIndex < ResultAggregates.All.Length
            ? ResultAggregates.All[KindBox.SelectedIndex]
            : ResultAggregateKind.Count;

    private string? GroupBy => GroupBox.SelectedIndex >= 0 ? _columns[GroupBox.SelectedIndex] : null;

    private string? ValueColumn =>
        SelectedKind == ResultAggregateKind.Count || ValueBox.SelectedIndex < 0
            ? null
            : _columns[ValueBox.SelectedIndex];

    /// <summary>COUNT needs no column; every other aggregate does, so the box says so.</summary>
    private void UpdateValueState()
    {
        var kind = SelectedKind;
        ValueBox.IsEnabled = kind != ResultAggregateKind.Count;
        NoteText.Text = kind switch
        {
            ResultAggregateKind.Count =>
                "COUNT rows per group — no value column needed, so every column of this result can be a group.",
            ResultAggregateKind.Sum or ResultAggregateKind.Avg =>
                $"{kind.Label()} skips empty cells; the column has to hold numbers. A text column is refused, not read as zero.",
            _ =>
                $"{kind.Label()} compares values in their own type — dates as dates, numbers as numbers."
        };
    }

    private PivotRequest? BuildRequest()
    {
        var group = GroupBy;
        if (group == null) return null;
        return new PivotRequest
        {
            GroupBy = group,
            Kind = SelectedKind,
            ValueColumn = ValueColumn
        };
    }

    private void UpdatePreview()
    {
        var group = GroupBy;
        if (group == null)
        {
            PreviewText.Text = "Pick a column to group by.";
            return;
        }
        try
        {
            var pivot = ResultGridService.Pivot(
                _table, group, SelectedKind, ValueColumn, _table.Filters);
            PreviewText.Text = pivot.RowCount == 0
                ? $"No group: {pivot.Title} — nothing is on screen to group."
                : ResultsExportService.ToMarkdown(pivot, PreviewGroups) +
                  (pivot.RowCount > PreviewGroups
                      ? $"\n… {pivot.RowCount - PreviewGroups:N0} more group(s) — the tab will hold all of them."
                      : string.Empty);
        }
        catch (Exception ex)
        {
            // The reason, before the operator commits to it.
            PreviewText.Text = ex.Message;
        }
    }

    /// <summary>First value the server put in that column, seen through its CLR type.</summary>
    private bool IsNumericColumn(string column) =>
        _table.Rows
            .Select(r => ResultGridService.ValueOf(r, column))
            .FirstOrDefault(v => v is not null)
            is byte or short or int or long or float or double or decimal;
}
