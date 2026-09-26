using Avalonia.Controls;
using SchemaCompare.Models;

namespace SchemaCompare.Views;

public partial class TablePropertiesDialog : Window
{
    private TablePropertiesDialog(TableProperties props)
    {
        InitializeComponent();
        Title = $"Table Properties — {props.Schema}.{props.Name}";
        DataContext = props;
        CloseBtn.Click += (_, _) => Close();
    }

    /// <summary>Read-only table properties view; completes when the user closes it.</summary>
    public static Task ShowAsync(Window owner, TableProperties props)
    {
        var dlg = new TablePropertiesDialog(props);
        return dlg.ShowDialog(owner);
    }
}
