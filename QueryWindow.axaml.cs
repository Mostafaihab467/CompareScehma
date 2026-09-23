using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using SchemaCompare.Controls;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare;

public partial class QueryWindow : Window
{
    private QueryViewModel? _vm;

    public QueryWindow()
    {
        InitializeComponent();

        _vm = new QueryViewModel();

        _vm.CopyToClipboardAsync = async text =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                    await ClipboardExtensions.SetTextAsync(cb, text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QueryWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
            }
        };

        DataContext = _vm;
        ClipboardGuard.Attach(this, message =>
        {
            if (_vm != null)
                _vm.StatusMessage = message;
        });
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AttachResultGridHandlers();
        ApplySqlHighlighting();
        SyncActiveEditorText();
        if (_vm != null)
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.PropertyChanged += Vm_PropertyChanged;
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(QueryViewModel.ActiveTab))
            SyncActiveEditorText();
    }

    // Fires each time a tab's editor control is materialized by the TabControl template.
    private void SqlEditor_Loaded(object? sender, RoutedEventArgs e)
    {
        ApplySqlHighlighting();
        SyncActiveEditorText();
        if (sender is TextEditor editor)
        {
            SqlCompletionProvider.Attach(editor);
            editor.KeyDown -= SqlEditor_KeyDown;
            editor.KeyDown += SqlEditor_KeyDown;
            ApplyEditorFontSize(editor);
        }
    }

    // Manual Ctrl+Space completion trigger (F5/Ctrl+Enter/Ctrl+T/Ctrl+W handled in OnKeyDown).
    private void SqlEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Space)
        {
            if (sender is TextEditor editor && editor.TextArea != null)
            {
                SqlCompletionProvider.Close();
                SqlCompletionProvider.Show(editor.TextArea, editor.CaretOffset);
                e.Handled = true;
            }
        }
    }

    private void ApplyEditorFontSize(TextEditor editor)
    {
        try
        {
            if (Application.Current?.TryGetResource("CodeFontSize", null, out var v) == true &&
                v is double size)
                editor.FontSize = size;
        }
        catch { }
    }

    /// <summary>
    /// TextEditor.Text is not a bindable property, so push/pull the active tab text manually.
    /// </summary>
    private void SyncActiveEditorText()
    {
        try
        {
            if (_vm?.ActiveTab == null) return;
            var editor = this.GetVisualDescendants().OfType<TextEditor>().FirstOrDefault();
            if (editor == null) return;
            editor.TextChanged -= ActiveEditor_TextChanged;
            if (editor.Text != _vm.ActiveTab.SqlText)
                editor.Text = _vm.ActiveTab.SqlText ?? string.Empty;
            editor.TextChanged += ActiveEditor_TextChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QueryWindow] Editor sync failed: {ex.Message}");
        }
    }

    private void ActiveEditor_TextChanged(object? sender, EventArgs e)
    {
        try
        {
            if (_vm?.ActiveTab != null && sender is TextEditor editor)
                _vm.ActiveTab.SqlText = editor.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QueryWindow] Editor read failed: {ex.Message}");
        }
    }

    private void ApplySqlHighlighting()
    {
        try
        {
            var definition = LoadTsqlHighlighting();
            if (definition == null) return;
            foreach (var editor in this.GetVisualDescendants().OfType<TextEditor>())
                editor.SyntaxHighlighting = definition;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QueryWindow] SQL highlighting unavailable: {ex.Message}");
        }
    }

    /// <summary>Loads the bundled T-SQL .xshd (VS dark colors); falls back to built-in SQL.</summary>
    private static IHighlightingDefinition? LoadTsqlHighlighting()
    {
        try
        {
            var asm = typeof(QueryWindow).Assembly;
            using var stream = asm.GetManifestResourceStream("SchemaCompare.Resources.TSQL.xshd");
            if (stream != null)
            {
                using var reader = System.Xml.XmlReader.Create(stream);
                var xshd = HighlightingLoader.LoadXshd(reader);
                return HighlightingLoader.Load(xshd, HighlightingManager.Instance);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QueryWindow] TSQL.xshd load failed: {ex.Message}");
        }
        return HighlightingManager.Instance.GetDefinition("SQL");
    }

    // -- Dynamic result columns (ItemsSource is List<Dictionary<string,object?>>) --
    // Result DataGrids live inside TabControl/DataTemplates, so re-attach after each render.
    private void AttachResultGridHandlers()
    {
        foreach (var grid in this.GetVisualDescendants().OfType<DataGrid>())
            WireResultGrid(grid);
        LayoutUpdated -= QueryWindow_LayoutUpdated;
        LayoutUpdated += QueryWindow_LayoutUpdated;
    }

    private void QueryWindow_LayoutUpdated(object? sender, EventArgs e)
    {
        foreach (var grid in this.GetVisualDescendants().OfType<DataGrid>())
            RebuildResultGridColumns(grid);
    }

    private void WireResultGrid(DataGrid grid)
    {
        grid.Loaded -= ResultGrid_Loaded;
        grid.Loaded += ResultGrid_Loaded;
        if (grid.IsLoaded)
            RebuildResultGridColumns(grid);
    }

    private void ResultGrid_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid)
            RebuildResultGridColumns(grid);
    }

    private static void RebuildResultGridColumns(DataGrid grid)
    {
        if (grid.Tag is not System.Collections.IEnumerable cols)
            return;
        var names = cols.Cast<object?>().Select(c => c?.ToString() ?? string.Empty)
            .Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (names.Count == 0)
            return;
        // Avoid rebuilding when the shape is unchanged. Compare the header's
        // text (headers are TextBlocks) — a broken guard here would rebuild on
        // every layout pass, invalidating layout again: infinite loop + crash.
        if (grid.Columns.Count == names.Count &&
            grid.Columns.Select(c => (c.Header as TextBlock)?.Text ?? c.Header?.ToString())
                .SequenceEqual(names))
            return;

        grid.Columns.Clear();
        foreach (var col in names)
        {
            // Header shows the real column name (used to look up cell values).
            var header = new TextBlock
            {
                Text = col,
                FontWeight = Avalonia.Media.FontWeight.Bold
            };
            ToolTip.SetTip(header, col);
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"[{col}]") { TargetNullValue = "(NULL)" },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star, 60, 400),
                IsReadOnly = true
            });
        }
        grid.SelectionChanged -= ResultGrid_SelectionChanged;
        grid.SelectionChanged += ResultGrid_SelectionChanged;
        // Cell focus changes don't raise SelectionChanged — track them too so the
        // "Column: value" inspector updates on plain arrow/click navigation.
        grid.CurrentCellChanged -= ResultGrid_CurrentCellChanged;
        grid.CurrentCellChanged += ResultGrid_CurrentCellChanged;
    }

    private static void ResultGrid_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (sender is DataGrid grid)
            InspectSelectedCell(grid);
    }

    /// <summary>
    /// SSMS-style: surface the focused cell as "Column: value" on the active tab,
    /// and keep the tab's SelectedResult pointing at the grid being inspected.
    /// Focused column comes from CurrentColumn; focused row from SelectedItem.
    /// Static because it is subscribed from the static BuildResultGrid template;
    /// the owning window/VM is resolved through the sender's visual root.
    /// </summary>
    private static void ResultGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid)
            InspectSelectedCell(grid);
    }

    private static void InspectSelectedCell(DataGrid grid)
    {
        try
        {
            var owner = (TopLevel.GetTopLevel(grid) as QueryWindow)?._vm;
            if (owner?.ActiveTab == null)
                return;
            var tab = owner.ActiveTab;
            var result = tab.Results.FirstOrDefault(r =>
                ReferenceEquals(r.Rows, grid.ItemsSource));
            if (result != null && !ReferenceEquals(tab.SelectedResult, result))
                tab.SelectedResult = result;

            string? cellText = null;
            if (grid.CurrentColumn is DataGridTextColumn textCol &&
                grid.SelectedItem is Dictionary<string, object?> row)
            {
                var colName = (textCol.Header as TextBlock)?.Text ?? textCol.Header?.ToString() ?? "?";
                row.TryGetValue(colName, out var value);
                cellText = $"{colName}: {(value == null ? "(NULL)" : value.ToString())}";
            }
            tab.SelectedCellText = cellText ?? "No cell selected.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QueryWindow] Cell inspect failed: {ex.Message}");
        }
    }

    // -- Keyboard shortcuts: F5/Ctrl+Enter execute, Ctrl+T new tab, Ctrl+W close --
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_vm?.ActiveTab is { } tab)
        {
            if (e.Key == Key.F5 || (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Enter))
            {
                var selection = GetActiveEditorSelection();
                _vm.ExecuteSelectionCommand.Execute(selection);
                e.Handled = true;
                return;
            }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.T)
            {
                _vm.NewTabCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.W)
            {
                _vm.CloseTabCommand.Execute(tab);
                e.Handled = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }

    /// <summary>Selected text of the active tab's editor (empty when nothing selected).</summary>
    private string GetActiveEditorSelection()
    {
        try
        {
            var editor = this.GetVisualDescendants().OfType<TextEditor>().FirstOrDefault();
            return editor?.SelectedText ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
