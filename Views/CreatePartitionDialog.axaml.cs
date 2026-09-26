using Avalonia.Controls;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.Views;

public partial class CreatePartitionDialog : Window
{
    private readonly TableMetadata _meta;
    private bool _finished;

    private CreatePartitionDialog(string schema, string table, TableMetadata meta)
    {
        InitializeComponent();
        _meta = meta;

        HeaderText.Text = $"Create Partition — {schema}.{table}";
        Title = $"Create Partition — {schema}.{table}";

        ColumnBox.ItemsSource = meta.Columns.Select(c => $"{c.Name}  ({c.DataType})").ToList();
        ColumnBox.SelectionChanged += (_, _) => OnColumnChanged();
        RangeBox.SelectionChanged += (_, _) => UpdateScript();
        FuncNameBox.TextChanged += (_, _) => UpdateScript();
        SchemeNameBox.TextChanged += (_, _) => UpdateScript();
        BoundariesBox.TextChanged += (_, _) => UpdateScript();
        FilegroupBox.TextChanged += (_, _) => UpdateScript();
        AlignCheck.IsCheckedChanged += (_, _) => UpdateScript();

        ScriptBtn.Click += (_, _) => Finish(executeNow: false);
        ExecuteBtn.Click += (_, _) => Finish(executeNow: true);
        CancelBtn.Click += (_, _) => Close(null);

        // Auto-select the first date-like or id column for convenience
        var seed = meta.Columns.FirstOrDefault(c =>
                       c.DataType.Contains("date", StringComparison.OrdinalIgnoreCase)) ??
                   meta.Columns.FirstOrDefault(c => c.IsPrimaryKey) ??
                   meta.Columns.FirstOrDefault();
        if (seed != null)
            ColumnBox.SelectedIndex = meta.Columns.IndexOf(seed);
    }

    public static Task<PartitionSpec?> ShowAsync(Window owner, string schema, string table, TableMetadata meta)
    {
        var dlg = new CreatePartitionDialog(schema, table, meta);
        return dlg.ShowDialog<PartitionSpec?>(owner);
    }

    private (string Name, string DataType)? SelectedColumn
    {
        get
        {
            if (ColumnBox.SelectedIndex < 0 || ColumnBox.SelectedIndex >= _meta.Columns.Count)
                return null;
            var c = _meta.Columns[ColumnBox.SelectedIndex];
            return (c.Name, c.DataType);
        }
    }

    private void OnColumnChanged()
    {
        var col = SelectedColumn;
        if (col == null) return;
        var (name, dataType) = col.Value;
        FuncNameBox.Text = $"PF_{_meta.Name}_{name}";
        SchemeNameBox.Text = $"PS_{_meta.Name}_{name}";
        UpdateAlignOption();
        UpdateScript();
    }

    private void UpdateAlignOption()
    {
        var clustered = _meta.Indexes.FirstOrDefault(i => i.TypeDesc == "CLUSTERED");
        var isHeap = clustered == null;
        AlignCheck.IsVisible = true;
        if (clustered != null)
        {
            AlignText.Text = $"Also move the table onto the scheme: rebuild clustered index {clustered.Name} " +
                             $"({string.Join(", ", clustered.KeyColumns)}) WITH (DROP_EXISTING = ON).";
            AlignCheck.IsChecked = true;
        }
        else
        {
            AlignText.Text = "The table is a heap — a new clustered index on the partition column will be created to align it.";
            AlignCheck.IsChecked = true;
        }
        _ = isHeap;
    }

    private PartitionSpec BuildSpec(bool executeNow)
    {
        var (colName, colType) = SelectedColumn ?? ("", "");
        var clustered = _meta.Indexes.FirstOrDefault(i => i.TypeDesc == "CLUSTERED");
        return new PartitionSpec
        {
            Schema = _meta.Schema,
            Table = _meta.Name,
            ColumnName = colName,
            ColumnDataType = colType,
            FunctionName = FuncNameBox.Text?.Trim() ?? "",
            SchemeName = SchemeNameBox.Text?.Trim() ?? "",
            RangeType = (RangeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "RIGHT",
            Boundaries = BoundariesBox.Text?
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length > 0)
                .ToList() ?? [],
            Filegroup = FilegroupBox.Text?.Trim() ?? "PRIMARY",
            AlignClusteredIndex = AlignCheck.IsChecked == true,
            ExistingClusteredIndexName = clustered?.Name,
            ExistingClusteredKeyColumns = clustered?.KeyColumns ?? [],
            TableIsHeap = clustered == null,
            ExecuteNow = executeNow
        };
    }

    private void UpdateScript()
    {
        try
        {
            Preview.Text = ManagerScriptBuilder.CreatePartition(BuildSpec(false));
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

        if (string.IsNullOrWhiteSpace(spec.ColumnName) ||
            string.IsNullOrWhiteSpace(spec.FunctionName) ||
            string.IsNullOrWhiteSpace(spec.SchemeName) ||
            spec.Boundaries.Count == 0)
        {
            if (spec.Boundaries.Count == 0) BoundariesBox.Focus();
            return;
        }

        try
        {
            _ = ManagerScriptBuilder.CreatePartition(spec); // validate boundary quoting
        }
        catch (FormatException)
        {
            BoundariesBox.Focus();
            return;
        }

        _finished = true;
        Close(spec);
    }
}
