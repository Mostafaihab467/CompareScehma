using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SchemaCompare.Models;
using SchemaCompare.ViewModels;

namespace SchemaCompare.Views;

/// <summary>
/// The command palette: type a few letters of a table, view, procedure or command, press Enter.
/// It is shown with <see cref="Window.Show"/> and hands back its choice through
/// <see cref="Completed"/> rather than as a dialog, so the host window stays live and a headless
/// run can answer it — a modal picker never answers under headless Avalonia.
/// </summary>
public partial class CommandPaletteWindow : Window
{
    private readonly CommandPaletteViewModel _vm;
    private readonly TaskCompletionSource<PaletteItem?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private PaletteItem? _chosen;

    public CommandPaletteWindow(IReadOnlyList<PaletteItem> items)
    {
        InitializeComponent();
        _vm = new CommandPaletteViewModel(items);
        DataContext = _vm;

        _vm.Accepted = item =>
        {
            _chosen = item;
            Close();
            return Task.CompletedTask;
        };
        Closed += (_, _) => _completion.TrySetResult(_chosen);
        Opened += (_, _) => SearchBox.Focus();
    }

    /// <summary>The row the operator chose, or null when the palette was dismissed.</summary>
    public Task<PaletteItem?> Completed => _completion.Task;

    public string SearchText
    {
        get => _vm.SearchText;
        set => _vm.SearchText = value;
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter or Key.Return:
                _vm.AcceptCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
        }
    }

    private void ResultList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        _vm.AcceptCommand.Execute(null);
        e.Handled = true;
    }

    private void MoveSelection(int delta)
    {
        if (_vm.Results.Count == 0) return;
        var index = _vm.Selected == null ? -1 : _vm.Results.IndexOf(_vm.Selected);
        var next = Math.Clamp(index + delta, 0, _vm.Results.Count - 1);
        _vm.Selected = _vm.Results[next];
        ResultList.ScrollIntoView(next);
    }
}
