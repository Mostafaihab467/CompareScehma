using Avalonia.Controls;
using SchemaCompare.Models;

namespace SchemaCompare.Views;

public partial class DbPropertiesDialog : Window
{
    private DbPropertiesDialog(DatabaseProperties props)
    {
        InitializeComponent();
        Title = $"Database Properties — {props.Name}";
        DataContext = props;
        CloseBtn.Click += (_, _) => Close();
    }

    /// <summary>Read-only properties view; completes when the user closes it.</summary>
    public static Task ShowAsync(Window owner, DatabaseProperties props)
    {
        var dlg = new DbPropertiesDialog(props);
        return dlg.ShowDialog(owner);
    }
}
