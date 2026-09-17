using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare;

public partial class BackupWindow : Window
{
    public BackupWindow()
    {
        InitializeComponent();
        ClipboardGuard.Attach(this, message =>
        {
            if (DataContext is MainViewModel vm)
                vm.BackupStatus = message;
        });
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || StorageProvider is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save local database backup",
            SuggestedFileName = vm.BackupSuggestedFileName,
            FileTypeChoices = [new FilePickerFileType("SQL Server BACPAC") { Patterns = ["*.bacpac"] }]
        });
        if (file is not null) vm.BackupDestinationPath = file.Path.LocalPath;
    }
}
