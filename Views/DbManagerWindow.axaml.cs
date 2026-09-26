using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Controls.Templates;
using SchemaCompare.Controls;
using SchemaCompare.Models;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare.Views;

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
                AppLog.Error("DbManagerWindow", ex, "Copy failed");
            }
        };

        _vm.ShowNewIndexDialogAsync  = (schema, table, meta) => NewIndexDialog.ShowAsync(this, schema, table, meta);
        _vm.ShowPartitionDialogAsync = (schema, table, meta) => CreatePartitionDialog.ShowAsync(this, schema, table, meta);
        _vm.ShowObjectDesignerAsync  = kind => ObjectDesignerDialog.ShowAsync(this, kind);
        _vm.ShowScriptConfirmAsync   = (title, warning, script) => ScriptActionDialog.ShowAsync(this, title, warning, script);
        _vm.ShowDbPropertiesAsync    = props => DbPropertiesDialog.ShowAsync(this, props);
        _vm.ShowTablePropertiesAsync = props => TablePropertiesDialog.ShowAsync(this, props);
        _vm.ShowDependenciesAsync    = deps => DependenciesDialog.ShowAsync(this, deps);
        _vm.PickBackupFileAsync      = PickBackupFileAsync;
        _vm.ShowRestoreDialogAsync   = draft => RestoreDatabaseDialog.ShowAsync(this, draft);
        _vm.ShowBackupDialogAsync    = draft => BackupDatabaseDialog.ShowAsync(this, draft);

        DataContext = _vm;

        // Avalonia 12 has no XAML hierarchical template type — build node
        // visuals here; Children is the lazy-loaded child collection.
        var tree = this.FindControl<TreeView>("ManagerTree");
        if (tree != null)
            tree.ItemTemplate = new FuncTreeDataTemplate<ManagerNode>(
                BuildNodePanel,
                node => node.Children);

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
    // Explorer tree context menu / double-click
    // -------------------------------------------------------------------------

    private static Control BuildNodePanel(ManagerNode node, INameScope? nameScope)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 1) };
        panel.Children.Add(new TextBlock { Text = node.Icon, FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = node.Label, FontWeight = FontWeight.SemiBold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        if (!string.IsNullOrEmpty(node.Detail))
        {
            IBrush? detailBrush = null;
            if (Application.Current?.TryGetResource("OnSurfaceVariant", null, out var res) == true && res is IBrush b)
                detailBrush = b;
            panel.Children.Add(new TextBlock
            {
                Text = node.Detail,
                FontSize = 11,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = detailBrush
            });
        }
        return panel;
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        var node = FindNodeUnder(e.Source as Control);
        if (node != null)
            _vm?.DoubleTapNodeCommand.Execute(node);
    }

    private async Task<string?> PickBackupFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select backup file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Backup files") { Patterns = ["*.bak"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null) return;
        var node = FindNodeUnder(e.Source as Control);
        if (node == null) return;

        var menu = new ContextMenu();
        void Add(string header, ICommand command, bool show)
        {
            if (!show) return;
            menu.Items.Add(new MenuItem { Header = header, Command = command, CommandParameter = node });
        }

        Add("📊  Properties",                _vm.DatabasePropertiesCommand, node.CanDatabaseProps);
        Add("📊  Properties",                _vm.TablePropertiesCommand,    node.CanGetTableProperties);
        Add("📉  Shrink Database…",          _vm.ShrinkDatabaseCommand,     node.CanShrink);
        Add("♻  Restore Database…",         _vm.RestoreDatabaseCommand,    node.CanRestore);
        Add("💾  Back Up Database…",         _vm.BackupDatabaseCommand,     node.CanBackup);
        if (menu.Items.Count > 0 && (node.CanDatabaseProps || node.CanGetTableProperties)) menu.Items.Add(new Separator());
        Add("📂  Open",                      _vm.OpenNodeCommand,         node.CanOpen);
        Add("📑  Select Top 1000 Rows",      _vm.SelectTopRowsCommand,    node.CanScriptData);
        Add("✏️  Edit Top 200 Rows",         _vm.EditTopRowsCommand,      node.CanScriptData);
        Add("🗃  New Index…",                _vm.NewIndexCommand,         node.CanNewIndex);
        Add("🧩  Create Partition…",         _vm.CreatePartitionCommand,  node.CanCreatePartition);
        Add(node.Kind switch
            {
                NodeKind.ViewsFolder => "➕  New View…",
                NodeKind.ProcsFolder => "➕  New Stored Procedure…",
                _ => "➕  New Table…"
            },
            _vm.NewObjectCommand, node.CanNewObject);
        Add("🕸  View Dependencies",         _vm.ViewDependenciesCommand,  node.CanViewDependencies);
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        Add("📄  Script as CREATE",          _vm.ScriptCreateCommand,     node.CanScriptCreate);
        Add("📄  Script as CREATE OR ALTER", _vm.ScriptCreateOrAlterCommand, node.CanScriptCreateOrAlter);
        Add("🔠  Script as SELECT",          _vm.ScriptSelectCommand,     node.CanScriptData);
        Add("➕  Script as INSERT",          _vm.ScriptInsertCommand,     node.CanScriptData);
        Add("✏️  Script as UPDATE",          _vm.ScriptUpdateCommand,     node.CanScriptData);
        Add("🗑  Script as DELETE",          _vm.ScriptDeleteCommand,     node.CanScriptDelete);
        Add("▶  Script as EXECUTE",          _vm.ScriptExecCommand,       node.CanScriptExec);
        Add("🗑  Script as DROP",            _vm.ScriptDropCommand,       node.CanScriptDrop);
        if (menu.Items.Count > 0 &&
            (node.CanRebuildIndex || node.CanDisableIndex || node.CanEnableIndex || node.CanDropIndex || node.CanUpdateStats))
            menu.Items.Add(new Separator());
        Add("♻  Rebuild",                    _vm.RebuildIndexCommand,     node.CanRebuildIndex);
        Add("⏸  Disable",                    _vm.DisableIndexCommand,     node.CanDisableIndex);
        Add("▶  Enable",                     _vm.EnableIndexCommand,      node.CanEnableIndex);
        Add("🗑  Drop",                       _vm.DropIndexCommand,        node.CanDropIndex);
        Add("📈  Update Statistics",         _vm.UpdateStatsCommand,      node.CanUpdateStats);
        if (menu.Items.Count > 0 &&
            (node.CanSetCurrentDb || node.CanStartJob || node.CanToggleJob))
            menu.Items.Add(new Separator());
        Add("🎯  Set As Current Database",   _vm.UseAsCurrentDatabaseCommand, node.CanSetCurrentDb);
        Add("▶  Start Job Now",              _vm.StartAgentJobCommand,        node.CanStartJob);
        Add(node.JobEnabled ? "⏸  Disable Job" : "▶  Enable Job",
            _vm.ToggleAgentJobCommand, node.CanToggleJob);
        if (node.CanRefresh)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            Add("↺  Refresh",                _vm.RefreshNodeCommand,      node.CanRefresh);
        }

        if (menu.Items.Count == 0) return;
        menu.Closed += (_, _) => menu.Items.Clear();
        menu.Open(this);
        e.Handled = true;
    }

    private static ManagerNode? FindNodeUnder(Control? source)
    {
        if (source == null) return null;
        return source.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(c => c.DataContext)
            .OfType<ManagerNode>()
            .FirstOrDefault();
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
