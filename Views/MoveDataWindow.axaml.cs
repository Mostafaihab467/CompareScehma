using Avalonia.Controls;
using Avalonia.Interactivity;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare.Views;

public partial class MoveDataWindow : Window
{
    public MoveDataWindow()
    {
        InitializeComponent();
        ClipboardGuard.Attach(this, message =>
        {
            if (DataContext is MainViewModel vm)
                vm.StatusMessage = message;
        });
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void CopyLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.CopyLogsToClipboardAsync();
    }
}
