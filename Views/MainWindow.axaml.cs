using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SchemaCompare.Models;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare.Views;

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
                AppLog.Error("MainWindow", ex, "Copy failed");
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
            }
        };

        ClipboardGuard.Attach(this, message =>
        {
            if (DataContext is MainViewModel current)
                current.StatusMessage = message;
        });

        DataContext = vm;
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
        vm.OpenDataCompareWindowAction = () =>
        {
            if (_dataCompareWindow is { IsVisible: true })
            {
                _dataCompareWindow.Activate();
                _dataCompareWindow.WindowState = WindowState.Normal;
                return;
            }
            var compareVm = new DataCompareViewModel();
            compareVm.InitializeFrom(vm.CurrentSourceConnection(), vm.CurrentTargetConnection());
            _dataCompareWindow = new DataCompareWindow { DataContext = compareVm };
            _dataCompareWindow.Closed += (_, _) => _dataCompareWindow = null;
            _dataCompareWindow.Show(this);
        };
        vm.OpenImportWindowAction = () =>
        {
            if (_importWindow is { IsVisible: true })
            {
                _importWindow.Activate();
                _importWindow.WindowState = WindowState.Normal;
                return;
            }
            var importVm = new ImportWizardViewModel();
            importVm.InitializeFrom(vm.CurrentSourceConnection());
            _importWindow = new ImportWizardWindow { DataContext = importVm };
            _importWindow.Closed += (_, _) => _importWindow = null;
            _importWindow.Show(this);
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
            diagramVm.InitializeFrom(vm.SourceServer, vm.SourceDatabase, vm.SourceAuth, vm.SourceUsername, vm.SourcePassword);
            _diagramWindow = new DiagramWindow { DataContext = diagramVm };
            _diagramWindow.Closed += (_, _) => _diagramWindow = null;
            _diagramWindow.Show(this);
        };
        vm.OpenDbManagerWindowAction = () =>
        {
            if (_dbManagerWindow is { IsVisible: true })
            {
                _dbManagerWindow.Activate();
                _dbManagerWindow.WindowState = WindowState.Normal;
                return;
            }
            _dbManagerWindow = new DbManagerWindow();
            _dbManagerWindow.Closed += (_, _) => _dbManagerWindow = null;
            _dbManagerWindow.Show(this);
        };
        vm.OpenQueryWindowAction = () =>
        {
            if (_queryWindow is { IsVisible: true })
            {
                _queryWindow.Activate();
                _queryWindow.WindowState = WindowState.Normal;
                return;
            }
            _queryWindow = new QueryWindow();
            _queryWindow.Closed += (_, _) => _queryWindow = null;
            _queryWindow.Show(this);
        };
        vm.OpenDbHealthWindowAction = () =>
        {
            if (_dbHealthWindow is { IsVisible: true })
            {
                _dbHealthWindow.Activate();
                _dbHealthWindow.WindowState = WindowState.Normal;
                return;
            }
            _dbHealthWindow = new DbHealthWindow();
            _dbHealthWindow.Closed += (_, _) => _dbHealthWindow = null;
            _dbHealthWindow.Show(this);
        };
        vm.OpenAboutWindowAction = () =>
        {
            if (_aboutWindow is { IsVisible: true })
            {
                _aboutWindow.Activate();
                return;
            }
            _aboutWindow = new AboutWindow();
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
            _aboutWindow.Show(this);
        };
    }

    private MoveDataWindow? _moveDataWindow;
    private DataCompareWindow? _dataCompareWindow;
    private ImportWizardWindow? _importWindow;
    private BackupWindow? _backupWindow;
    private DiagramWindow? _diagramWindow;
    private DbManagerWindow? _dbManagerWindow;
    private QueryWindow? _queryWindow;
    private DbHealthWindow? _dbHealthWindow;
    private AboutWindow? _aboutWindow;

    private async void CopyScript_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.DisplayedScript) &&
                TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                await ClipboardExtensions.SetTextAsync(cb, vm.DisplayedScript);
        }
        catch (Exception ex)
        {
            AppLog.Error("MainWindow", ex, "Copy failed");
            if (DataContext is MainViewModel vm)
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
        }
    }

    /// <summary>
    /// Saves whichever script the pane is showing. The DOWN script is the one this exists
    /// for: it only describes the pre-deploy state, so it has to survive on disk.
    /// </summary>
    private async void SaveScript_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || StorageProvider is null) return;
        if (string.IsNullOrEmpty(vm.DisplayedScript)) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = vm.ShowingRollbackScript ? "Save Rollback Script" : "Save Deployment Script",
            SuggestedFileName = vm.ScriptSuggestedFileName,
            DefaultExtension = "sql",
            FileTypeChoices = [new FilePickerFileType("SQL scripts") { Patterns = ["*.sql"] }]
        });
        if (file is null) return;

        try
        {
            await File.WriteAllTextAsync(file.Path.LocalPath, vm.DisplayedScript);
            vm.StatusMessage = $"Script saved to {file.Name}";
        }
        catch (Exception ex)
        {
            AppLog.Error("MainWindow", ex, "Script save failed");
            vm.StatusMessage = $"Could not save the script: {ex.Message}";
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
            AppLog.Error("MainWindow", ex, "Copy failed");
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
