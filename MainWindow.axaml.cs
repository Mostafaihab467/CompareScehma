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
        DataContext = new MainViewModel();
    }

    private async void CopyScript_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.FullDeployScript) && TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            await ClipboardExtensions.SetTextAsync(cb, vm.FullDeployScript);
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
}
