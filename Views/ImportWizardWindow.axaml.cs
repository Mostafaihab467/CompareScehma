using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SchemaCompare.ViewModels;

namespace SchemaCompare.Views;

public partial class ImportWizardWindow : Window
{
    public ImportWizardWindow()
    {
        InitializeComponent();

        // The owner assigns DataContext after this constructor runs.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ImportWizardViewModel vm) return;
            vm.PickOpenFileAsync = PickOpenFileAsync;
            vm.ShowScriptConfirmAsync = (title, warning, script) =>
                ScriptActionDialog.ShowAsync(this, title, warning, script);
        };
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportWizardViewModel vm || vm.PickOpenFileAsync == null) return;
        var path = await vm.PickOpenFileAsync();
        if (!string.IsNullOrWhiteSpace(path)) vm.FilePath = path;
    }

    private async Task<string?> PickOpenFileAsync()
    {
        if (StorageProvider is null) return null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Delimited file to import",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Delimited files") { Patterns = ["*.csv", "*.tsv", "*.txt"] },
                new FilePickerFileType("All files") { Patterns = ["*.*"] }
            ]
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
