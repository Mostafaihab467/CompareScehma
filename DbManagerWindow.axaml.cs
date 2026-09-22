using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input.Platform;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare;

public partial class DbManagerWindow : Window
{
    private DbManagerViewModel? _vm;

    public DbManagerWindow()
    {
        InitializeComponent();

        _vm = new DbManagerViewModel();

        _vm.CopyToClipboardAsync = async text =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                    await ClipboardExtensions.SetTextAsync(cb, text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DbManagerWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
            }
        };

        DataContext = _vm;

        // Rebuild DataGrid columns whenever the column list changes
        _vm.TableColumnNames.CollectionChanged  += (_, _) => RebuildDataGridColumns();
        _vm.ExecResultColumns.CollectionChanged += (_, _) => RebuildExecGridColumns();

        // Commit cell edits to the VM so "Save Changes" can flush them to the DB
        var grid = this.FindControl<DataGrid>("DataGrid");
        if (grid != null)
        {
            grid.BeginningEdit += OnDataGridBeginningEdit;
            grid.CellEditEnding += OnDataGridCellEditEnding;
        }
    }

    private void OnDataGridBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        // Snapshot the row BEFORE the editor commits, so the WHERE clause uses original PKs.
        // NOTE: in Avalonia e.Row is a DataGridRow (visual) — the item is its DataContext.
        if (_vm != null && e.Row.DataContext is Dictionary<string, object?> row)
            _vm.SnapshotRow(row);
    }

    private void OnDataGridCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (_vm == null) return;
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.DataContext is not Dictionary<string, object?> row) return;

        // Bound columns are created in code-behind with Header = column name
        var columnName = (e.Column as DataGridBoundColumn)?.Header?.ToString() ?? e.Column?.Header?.ToString();
        if (string.IsNullOrWhiteSpace(columnName)) return;
        if (e.EditingElement is not TextBox tb) return;

        _vm.SnapshotRow(row); // no-op if BeginningEdit already snapped

        // Compare against original value to avoid pointless writes
        var currentValue = _vm.GetOriginalCellValue(row, columnName);
        var originalText = currentValue == null || currentValue is DBNull
            ? string.Empty
            : currentValue.ToString() ?? string.Empty;
        if (string.Equals(originalText, tb.Text, StringComparison.Ordinal)) return;

        _vm.CaptureCellEdit(row, columnName, tb.Text);
    }

    // -------------------------------------------------------------------------
    // Dynamic DataGrid columns (needed because ItemsSource is List<Dictionary<string,object?>>)
    // -------------------------------------------------------------------------

    private void RebuildDataGridColumns()
    {
        var grid = this.FindControl<DataGrid>("DataGrid");
        if (grid == null || _vm == null) return;

        grid.Columns.Clear();
        foreach (var col in _vm.TableColumnNames)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header    = col,
                Binding   = new Binding($"[{col}]") { TargetNullValue = "(NULL)" },
                Width     = new DataGridLength(120),
                IsReadOnly = false
            });
        }
    }

    private void RebuildExecGridColumns()
    {
        var grid = this.FindControl<DataGrid>("ExecDataGrid");
        if (grid == null || _vm == null) return;

        grid.Columns.Clear();
        foreach (var col in _vm.ExecResultColumns)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header    = col,
                Binding   = new Binding($"[{col}]") { TargetNullValue = "(NULL)" },
                Width     = new DataGridLength(120),
                IsReadOnly = true
            });
        }
    }
}
