using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SchemaCompare.Models;
using SchemaCompare.ViewModels;

namespace SchemaCompare;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();

        // Wire clipboard helper so the ViewModel can copy text without holding a window reference
        vm.CopyToClipboardAsync = async text =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                await ClipboardExtensions.SetTextAsync(cb, text);
        };

        DataContext = vm;
    }

    private async void CopyScript_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.FullDeployScript) &&
            TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            await ClipboardExtensions.SetTextAsync(cb, vm.FullDeployScript);
    }

    private async void CopyLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.CopyLogsToClipboardAsync();
    }

    private void DiffItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: SchemaDiffItem diff } && DataContext is MainViewModel vm)
            vm.SelectedDiff = diff;
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.ShowApplyConfirmation = true;
    }

    /// <summary>
    /// Intercepts Ctrl+X (cut) on read-only TextBoxes and converts it to a copy
    /// so the app does not crash when the user tries to cut from a read-only code box.
    /// </summary>
    private async void CodeBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.X)
        {
            // Treat cut as copy for read-only boxes
            if (sender is TextBox tb && TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            {
                var selected = tb.SelectedText;
                if (!string.IsNullOrEmpty(selected))
                    await ClipboardExtensions.SetTextAsync(cb, selected);
                else if (!string.IsNullOrEmpty(tb.Text))
                    await ClipboardExtensions.SetTextAsync(cb, tb.Text);
            }
            e.Handled = true;
        }
    }
}
