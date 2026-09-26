using Avalonia.Controls;
using SchemaCompare.Models;

namespace SchemaCompare.Views;

public partial class DependenciesDialog : Window
{
    private DependenciesDialog(ObjectDependencies deps)
    {
        InitializeComponent();
        Title = $"Object Dependencies — {deps.FullName}";
        DataContext = deps;
        CloseBtn.Click += (_, _) => Close();
    }

    /// <summary>Read-only dependency view for one catalog object.</summary>
    public static Task ShowAsync(Window owner, ObjectDependencies deps)
    {
        var dlg = new DependenciesDialog(deps);
        return dlg.ShowDialog(owner);
    }
}
