using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SchemaCompare.Models;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();

        vm.CopyToClipboardAsync = async text =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                    await ClipboardExtensions.SetTextAsync(cb, text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
            }
        };

        ClipboardGuard.Attach(this, message =>
        {
            if (DataContext is MainViewModel current)
                current.StatusMessage = message;
        });

        DataContext = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ShowTargetPane) or nameof(MainViewModel.ShowSourcePane))
                ApplyDetailPaneRows(vm);
        };
        ApplyDetailPaneRows(vm);
        vm.OpenMoveDataWindowAction = () =>
        {
            if (_moveDataWindow is { IsVisible: true })
            {
                _moveDataWindow.Activate();
                _moveDataWindow.WindowState = WindowState.Normal;
                return;
            }
            _moveDataWindow = new MoveDataWindow { DataContext = vm };
            _moveDataWindow.Closed += (_, _) => _moveDataWindow = null;
            _moveDataWindow.Show(this);
        };
        vm.OpenBackupWindowAction = () =>
        {
            if (_backupWindow is { IsVisible: true })
            {
                _backupWindow.Activate();
                _backupWindow.WindowState = WindowState.Normal;
                return;
            }
            _backupWindow = new BackupWindow { DataContext = vm };
            _backupWindow.Closed += (_, _) => _backupWindow = null;
            _backupWindow.Show(this);
        };
        vm.OpenDiagramWindowAction = () =>
        {
            if (_diagramWindow is { IsVisible: true })
            {
                _diagramWindow.Activate();
                _diagramWindow.WindowState = WindowState.Normal;
                return;
            }
            var diagramVm = new DiagramViewModel();
            diagramVm.InitializeFrom(vm.SourceServer, vm.SourceDatabase, vm.SourceUseWindowsAuth, vm.SourceUsername, vm.SourcePassword);
            _diagramWindow = new DiagramWindow { DataContext = diagramVm };
            _diagramWindow.Closed += (_, _) => _diagramWindow = null;
            _diagramWindow.Show(this);
        };
    }

    private MoveDataWindow? _moveDataWindow;
    private BackupWindow? _backupWindow;
    private DiagramWindow? _diagramWindow;

    /// <summary>
    /// A hidden detail pane must not keep its half of the split: collapse its
    /// grid row to zero so the visible pane takes the full space. Called on
    /// toggle and once at startup (FindControl needs the loaded visual tree).
    /// </summary>
    private void ApplyDetailPaneRows(MainViewModel vm)
    {
        var grid = this.FindControl<Grid>("DetailSplitGrid");
        if (grid is null || grid.RowDefinitions.Count < 3) return;
        grid.RowDefinitions[0].Height = vm.ShowTargetPane
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        grid.RowDefinitions[2].Height = vm.ShowSourcePane
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
    }

    private async void CopyScript_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.FullDeployScript) &&
                TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                await ClipboardExtensions.SetTextAsync(cb, vm.FullDeployScript);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
            if (DataContext is MainViewModel vm)
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
        }
    }

    private async void CopyLogs_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm)
                await vm.CopyLogsToClipboardAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
            if (DataContext is MainViewModel vm)
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
        }
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
