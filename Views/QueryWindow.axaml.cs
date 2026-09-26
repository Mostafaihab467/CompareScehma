using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using SchemaCompare.Controls;
using SchemaCompare.Models;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare.Views;

public partial class QueryWindow : Window
{
    private QueryViewModel? _vm;
    private QueryHistoryWindow? _historyWindow;

    public QueryWindow()
    {
        InitializeComponent();

        _vm = new QueryViewModel();

        _vm.CopyToClipboardAsync = async text =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                    return await ClipboardSafety.CopySelectionAsync(text, cb);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"[QueryWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
            }
            return false;
        };

        DataContext = _vm;
        _vm.ShowKeywordExplainer = keyword => _ = AggregationExplainerDialog.ShowAsync(this, keyword);
        _vm.ShowQueryExplanation = sql => _ = QueryExplainDialog.ShowAsync(this, sql);
    _vm.PickSavePathAsync = async suggested =>
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Results As CSV",
            SuggestedFileName = suggested,
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV files") { Patterns = ["*.csv"] }]
        });
        return file?.Path.LocalPath;
    };
    _vm.PickOpenSqlPathAsync = async () =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open SQL Script",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SQL scripts") { Patterns = ["*.sql", "*.txt"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    };
    _vm.PickSaveSqlPathAsync = async suggested =>
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Script As",
            SuggestedFileName = suggested,
            DefaultExtension = "sql",
            FileTypeChoices = [new FilePickerFileType("SQL scripts") { Patterns = ["*.sql"] }]
        });
        return file?.Path.LocalPath;
    };

    // Dropping a .sql file from Explorer opens it in a tab.
    AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = FirstDroppedSqlFile(e) == null ? DragDropEffects.None : DragDropEffects.Copy);
    AddHandler(DragDrop.DropEvent, OnDroppedFile);

    // Query history window + "insert at caret" back into the active editor.
    _vm.OpenHistoryWindowAction = () =>
    {
        if (_historyWindow is { IsVisible: true })
        {
            _historyWindow.Activate();
            return;
        }
        _historyWindow = new QueryHistoryWindow(_vm);
        _historyWindow.Closed += (_, _) => _historyWindow = null;
        _historyWindow.Show(this);
    };
    _vm.InsertSqlAtCaret = sql =>
    {
        var editor = this.GetVisualDescendants().OfType<TextEditor>().FirstOrDefault();
        if (editor?.Document == null) return;
        var offset = Math.Clamp(editor.CaretOffset, 0, editor.Document.TextLength);
        editor.Document.Insert(offset, sql);
        editor.Focus();
    };

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
            if (_vm.SelectedConnection != null && !_vm.IsConnected)
                _vm.ConnectCommand.Execute(null);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _vm?.PersistSession();
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
        TextEditor? editor = sender as TextEditor;
        if (sender is SqlHighlightedEditor host)
        {
            host.EnableLint = true;
            host.AttachCompletion();
            editor = host.InnerEditor;
        }
        if (editor != null)
        {
            SqlCompletionProvider.Attach(editor);
            editor.KeyDown -= SqlEditor_KeyDown;
            editor.KeyDown += SqlEditor_KeyDown;
            ApplyEditorFontSize(editor);
            WireEditorClipboard(editor);
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
    /// Ctrl+C / Ctrl+X / Ctrl+V for the SQL editor go through ClipboardSafety.
    /// AvaloniaEdit's built-in editing commands call the platform clipboard
    /// unguarded — the same async-void COMException path that made Ctrl+C close
    /// the whole app for TextBoxes (see ClipboardSafety) — so intercept the
    /// gestures in the tunnel phase, mark them handled and redo them safely.
    /// </summary>
    private void WireEditorClipboard(TextEditor editor)
    {
        editor.TextArea.RemoveHandler(InputElement.KeyDownEvent, EditorClipboardKeyDown);
        editor.TextArea.AddHandler(InputElement.KeyDownEvent, EditorClipboardKeyDown, RoutingStrategies.Tunnel);
    }

    private async void EditorClipboardKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (e.Handled || sender is not TextArea area) return;
            var hotkeys = Application.Current?.PlatformSettings?.HotkeyConfiguration;
            if (hotkeys == null) return;
            var editor = area.GetVisualAncestors().OfType<TextEditor>().FirstOrDefault();
            if (editor == null) return;

            if (hotkeys.Copy.Any(g => g.Matches(e)))
            {
                var text = editor.SelectedText;
                if (string.IsNullOrEmpty(text)) return;
                e.Handled = true;
                if (!await ClipboardSafety.CopySelectionAsync(text, TopLevel.GetTopLevel(area)?.Clipboard))
                    ReportClipboardFailure("Copy");
            }
            else if (hotkeys.Cut.Any(g => g.Matches(e)))
            {
                var text = editor.SelectedText;
                if (string.IsNullOrEmpty(text)) return;
                e.Handled = true;
                if (!await ClipboardSafety.CutSelectionAsync(
                        text, () => editor.SelectedText = string.Empty,
                        editor.IsReadOnly, TopLevel.GetTopLevel(area)?.Clipboard))
                    ReportClipboardFailure("Cut");
            }
            else if (hotkeys.Paste.Any(g => g.Matches(e)) && !editor.IsReadOnly)
            {
                e.Handled = true;
                var (ok, text) = await ClipboardSafety.TakePasteTextAsync(TopLevel.GetTopLevel(area)?.Clipboard);
                if (!ok)
                {
                    ReportClipboardFailure("Paste");
                    return;
                }
                if (string.IsNullOrEmpty(text)) return;
                editor.SelectedText = ClipboardSafety.SanitizePasteText(text, acceptsReturn: true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[QueryWindow] Editor clipboard failed ({ex.GetType().Name}): {ex.Message}");
        }
    }

    private void ReportClipboardFailure(string operation)
    {
        if (_vm != null)
            _vm.StatusMessage = $"{operation} failed: the system clipboard is unavailable (another app may be holding it). Nothing was changed.";
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
            foreach (var host in this.GetVisualDescendants().OfType<SqlHighlightedEditor>())
                host.EnableLint = true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[QueryWindow] Editor sync failed: {ex.Message}");
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
            AppLog.Warn($"[QueryWindow] Editor read failed: {ex.Message}");
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
            AppLog.Warn($"[QueryWindow] SQL highlighting unavailable: {ex.Message}");
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
            AppLog.Warn($"[QueryWindow] TSQL.xshd load failed: {ex.Message}");
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
        if (grid.Tag is not QueryResultTable result)
            return;
        var names = result.Columns
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
                // Fixed pixel width fitted to the bold header and cell content
                // (star sizing degenerates under unconstrained measure: the first
                // column swallows the viewport). Wide grids scroll horizontally.
                Width = new DataGridLength(MeasureColumnWidth(result, col)),
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

    private const double ResultGridFontSize = 12; // matches the DataGrid FontSize in XAML

    /// <summary>
    /// Pixel width fitting the bold header and the widest cell (sampled over the
    /// first 30 rows), clamped so short columns stay clickable and very long
    /// text (JSON, descriptions) cannot blow up the grid — it clips instead.
    /// The sample is small because this runs on the UI thread per rebuild.
    /// </summary>
    private static double MeasureColumnWidth(QueryResultTable result, string column)
    {
        var width = MeasureText(column, bold: true);
        var sample = Math.Min(result.Rows.Count, 30);
        for (var i = 0; i < sample; i++)
        {
            if (!result.Rows[i].TryGetValue(column, out var value) || value == null)
                continue;
            var text = value switch
            {
                string s => s.Split('\n')[0],
                byte[] => "System.Byte[]",
                _ => value.ToString() ?? string.Empty
            };
            if (text.Length > 0)
                width = Math.Max(width, MeasureText(text, bold: false));
        }
        return Math.Clamp(width + 26, 70, 360);
    }

    private static double MeasureText(string text, bool bold)
    {
        try
        {
            var formatted = new Avalonia.Media.FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                Avalonia.Media.FlowDirection.LeftToRight,
                new Avalonia.Media.Typeface(Avalonia.Media.FontFamily.Default,
                    Avalonia.Media.FontStyle.Normal,
                    bold ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal),
                ResultGridFontSize,
                null);
            return formatted.Width;
        }
        catch
        {
            return text.Length * (bold ? 8.2 : 7.0);
        }
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
            AppLog.Warn($"[QueryWindow] Cell inspect failed: {ex.Message}");
        }
    }

    // -- Keyboard shortcuts: F5/Ctrl+Enter execute, Ctrl+T new tab, Ctrl+W close,
    //    Ctrl+O / Ctrl+S / Ctrl+Shift+S for .sql files --
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+M folding, Ctrl+B/K bookmarks, Ctrl+G goto line: consumed by the
        // editor itself while it has focus; forward here when focus is on the
        // result grid so the chords still act on the active tab's script.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Key == Key.Escape)
        {
            var host = this.GetVisualDescendants().OfType<SqlHighlightedEditor>()
                .FirstOrDefault(h => h.IsVisible);
            if (host != null && host.HandleEditorChord(e))
            {
                e.Handled = true;
                return;
            }
        }
        if (_vm != null && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.O)
            {
                _vm.OpenFileCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.S)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _vm.SaveFileAsCommand.Execute(null);
                else _vm.SaveFileCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }
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
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.L)
            {
                _vm.EstimatedPlanCommand.Execute(tab);
                e.Handled = true;
                return;
            }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                && e.Key == Key.F)
            {
                _vm.FormatSqlCommand.Execute(null);
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

    private void OnDroppedFile(object? sender, DragEventArgs e)
    {
        var path = FirstDroppedSqlFile(e);
        if (path != null) _vm?.OpenSqlFile(path);
    }

    /// <summary>First dropped file that looks like a script; null when the drag holds none.</summary>
    private static string? FirstDroppedSqlFile(DragEventArgs e)
    {
        try
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files == null) return null;
            foreach (var file in files)
            {
                var path = file.Path.LocalPath;
                if (path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[QueryWindow] Dropped items unreadable ({ex.GetType().Name}): {ex.Message}");
            return null;
        }
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

    // ────────────────────────────────────────────────────────────────────
    // Query Constructor dialog launcher
    // ────────────────────────────────────────────────────────────────────
    private QueryBuilderWindow? _builderWindow;

    private void OnOpenQueryBuilderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (_builderWindow is { IsVisible: true })
        {
            _builderWindow.Activate();
            return;
        }
        var builderVm = new QueryBuilderViewModel(_vm);
        _builderWindow = new QueryBuilderWindow(builderVm);
        _builderWindow.Closed += (_, _) =>
        {
            _builderWindow = null;
            // After the dialog applies, re-sync the (possibly new) active tab's
            // SQL into the editor control — the tab selection may have changed
            // inside NewTab()/ReplaceActiveTabSql() during the apply.
            SyncActiveEditorText();
        };
        _builderWindow.Show(this);
    }
}
