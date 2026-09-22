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
