using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare.Views;

public partial class DataCompareWindow : Window
{
    public DataCompareWindow()
    {
        InitializeComponent();

        // The owner assigns DataContext after this constructor runs.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DataCompareViewModel vm)
            {
                vm.PickSaveFileAsync = PickSaveFileAsync;
                vm.CopyToClipboardAsync = CopyToClipboardAsync;
            }
        };

        ClipboardGuard.Attach(this, message =>
        {
            if (DataContext is DataCompareViewModel current)
                current.StatusMessage = message;
        });
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async Task<string?> PickSaveFileAsync(string suggestedFileName, string extension)
    {
        if (StorageProvider is null) return null;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save data synchronization script",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = [new FilePickerFileType("T-SQL script") { Patterns = ["*.sql"] }],
        });
        return file?.Path.LocalPath;
    }

    private async Task CopyToClipboardAsync(string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                await ClipboardExtensions.SetTextAsync(cb, text);
        }
        catch (Exception ex)
        {
            AppLog.Error("DataCompareWindow", ex, "Copy failed");
            if (DataContext is DataCompareViewModel vm)
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
        }
    }
}
