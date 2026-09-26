using Avalonia.Controls;
using Avalonia.Threading;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare.Views;

public partial class DbHealthWindow : Window
{
    private DispatcherTimer? _timer;

    public DbHealthWindow()
    {
        InitializeComponent();
        var vm = new DbHealthViewModel();
        DataContext = vm;
        vm.CopyToClipboardAsync = async text =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                    return await ClipboardSafety.CopySelectionAsync(text, cb);
            }
            catch
            {
                return false;
            }
            return false;
        };

        vm.ConfirmScriptAsync = (title, warning, script) =>
            ScriptActionDialog.ShowAsync(this, title, warning, script);

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DbHealthViewModel.IntervalSeconds) or nameof(DbHealthViewModel.IsPaused))
                RestartTimer();
        };

        Loaded += async (_, _) =>
        {
            if (vm.SelectedConnection != null && !vm.IsConnected)
                vm.ConnectCommand.Execute(null);
            RestartTimer();
        };

        Closed += (_, _) =>
        {
            _timer?.Stop();
            vm.Shutdown();
        };
    }

    private void RestartTimer()
    {
        _timer?.Stop();
        if (DataContext is not DbHealthViewModel vm || vm.IsPaused || vm.IntervalSeconds <= 0)
            return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(vm.IntervalSeconds) };
        _timer.Tick += async (_, _) => await vm.RefreshTickAsync(forceSlow: false);
        _timer.Start();
    }
}
