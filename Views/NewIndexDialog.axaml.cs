using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.Views;

public partial class NewIndexDialog : Window
{
    private readonly TableMetadata _meta;
    private readonly ObservableCollection<string> _keyColumns = [];
    private readonly ObservableCollection<string> _incColumns = [];
    private bool _finished;

    private NewIndexDialog(string schema, string table, TableMetadata meta)
    {
        InitializeComponent();
        _meta = meta;

        HeaderText.Text = $"New Index — {schema}.{table}";
        Title = $"New Index — {schema}.{table}";
        NameBox.Text = $"IDX_{table}";

        var names = meta.Columns.Select(c => c.Name).ToList();
        KeyAvailBox.ItemsSource = names;
        IncAvailBox.ItemsSource = names;
        KeyList.ItemsSource = _keyColumns;
        IncList.ItemsSource = _incColumns;

        _keyColumns.CollectionChanged += (_, _) => UpdateAvailable();
        _incColumns.CollectionChanged += (_, _) => UpdateAvailable();

        NameBox.TextChanged += (_, _) => UpdateScript();
        TypeBox.SelectionChanged += (_, _) => { UpdateOptionStates(); UpdateScript(); };
        UniqueCheck.IsCheckedChanged += (_, _) => UpdateScript();
        FilterBox.TextChanged += (_, _) => UpdateScript();
        FillFactorBox.TextChanged += (_, _) => UpdateScript();

        AddKeyBtn.Click += (_, _) => AddFrom(KeyAvailBox, _keyColumns);
        AddIncBtn.Click += (_, _) => AddFrom(IncAvailBox, _incColumns);
        KeyRemoveBtn.Click += (_, _) => RemoveSelected(KeyList, _keyColumns);
        IncRemoveBtn.Click += (_, _) => RemoveSelected(IncList, _incColumns);
        KeyUpBtn.Click += (_, _) => MoveSelected(KeyList, _keyColumns, -1);
        KeyDownBtn.Click += (_, _) => MoveSelected(KeyList, _keyColumns, 1);

        ScriptBtn.Click += (_, _) => Finish(executeNow: false);
        ExecuteBtn.Click += (_, _) => Finish(executeNow: true);
        CancelBtn.Click += (_, _) => Close(null);
    }

    public static Task<IndexSpec?> ShowAsync(Window owner, string schema, string table, TableMetadata meta)
    {
        var dlg = new NewIndexDialog(schema, table, meta);
        return dlg.ShowDialog<IndexSpec?>(owner);
    }

    // ─── Column list helpers ─────────────────────────────────────────────────

    private void AddFrom(ComboBox box, ObservableCollection<string> target)
    {
        if (box.SelectedItem is string col && !target.Contains(col))
            target.Add(col);
        box.SelectedItem = null;
    }

    private void RemoveSelected(ListBox list, ObservableCollection<string> target)
    {
        if (list.SelectedItem is string col)
        {
            target.Remove(col);
            list.SelectedItem = null;
        }
    }

    private void MoveSelected(ListBox list, ObservableCollection<string> target, int delta)
    {
        if (list.SelectedItem is not string col) return;
        var i = target.IndexOf(col);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= target.Count) return;
        target.Move(i, j);
        list.SelectedItem = col;
    }

    private void UpdateAvailable()
    {
        var all = _meta.Columns.Select(c => c.Name).ToList();
        KeyAvailBox.ItemsSource = all.Where(c => !_keyColumns.Contains(c)).ToList();
        IncAvailBox.ItemsSource = all.Where(c => !_incColumns.Contains(c) && !_keyColumns.Contains(c)).ToList();
        UpdateScript();
    }

    private string SelectedType => (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Nonclustered";

    private void UpdateOptionStates()
    {
        var t = SelectedType;
        var isColumnstore = t.Contains("Columnstore", StringComparison.OrdinalIgnoreCase);
        KeyAvailBox.IsEnabled = t is not "Clustered Columnstore";
        AddKeyBtn.IsEnabled = KeyAvailBox.IsEnabled;
        IncAvailBox.IsEnabled = !isColumnstore;
        AddIncBtn.IsEnabled = !isColumnstore;
        FilterBox.IsEnabled = t is "Nonclustered" or "Nonclustered Columnstore";
        UniqueCheck.IsEnabled = !isColumnstore;

        NoteText.IsVisible = t is "Clustered Columnstore" or "Clustered";
        if (t == "Clustered Columnstore")
            NoteText.Text = "Clustered columnstore indexes include all columns and cannot coexist with another clustered index.";
        else if (t == "Clustered")
            NoteText.Text = _meta.Indexes.Any(i => i.TypeDesc == "CLUSTERED")
                ? $"The table already has a clustered index ({_meta.Indexes.First(i => i.TypeDesc == "CLUSTERED").Name}) — SQL Server allows only one."
                : null;
    }

    // ─── Script generation ───────────────────────────────────────────────────

    private IndexSpec BuildSpec(bool executeNow)
    {
        int.TryParse(FillFactorBox.Text?.Trim(), out var ff);
        return new IndexSpec
        {
            Schema = _meta.Schema,
            Table = _meta.Name,
            IndexName = NameBox.Text?.Trim() ?? "",
            IndexType = SelectedType,
            IsUnique = UniqueCheck.IsChecked == true,
            UseFillFactor = ff > 0,
            FillFactor = ff,
            Filter = string.IsNullOrWhiteSpace(FilterBox.Text) ? null : FilterBox.Text.Trim(),
            KeyColumns = [.. _keyColumns],
            IncludedColumns = [.. _incColumns],
            ExecuteNow = executeNow
        };
    }

    private void UpdateScript()
    {
        try
        {
            Preview.Text = ManagerScriptBuilder.CreateIndex(BuildSpec(false));
        }
        catch (Exception ex)
        {
            Preview.Text = $"-- {ex.Message}";
        }
    }

    private void Finish(bool executeNow)
    {
        if (_finished) return;
        var spec = BuildSpec(executeNow);

        if (string.IsNullOrWhiteSpace(spec.IndexName))
        {
            NameBox.Focus();
            return;
        }
        if (spec.IndexType is not ("Clustered Columnstore") && spec.KeyColumns.Count == 0)
        {
            KeyAvailBox.Focus();
            return;
        }

        _finished = true;
        Close(spec);
    }
}
