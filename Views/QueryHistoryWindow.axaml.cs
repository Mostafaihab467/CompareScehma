using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SchemaCompare.ViewModels;

namespace SchemaCompare.Views;

public partial class QueryHistoryWindow : Window
{
    private readonly QueryViewModel _vm;

    public QueryHistoryWindow(QueryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        DetailEditor.Text = vm.SelectedHistoryEntry?.Sql ?? string.Empty;
        vm.PropertyChanged += VmOnPropertyChanged;
        Closed += (_, _) => vm.PropertyChanged -= VmOnPropertyChanged;

        Opened += (_, _) => SearchBox.Focus();

        CopyButton.Click += (_, _) => _vm.CopyHistoryCommand.Execute(null);
        InsertButton.Click += (_, _) => InsertSelected();
        ClearButton.Click += (_, _) => _vm.ClearHistoryCommand.Execute(null);
        CloseButton.Click += (_, _) => Close();
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QueryViewModel.SelectedHistoryEntry))
            DetailEditor.Text = _vm.SelectedHistoryEntry?.Sql ?? string.Empty;
    }

    private void HistoryList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        InsertSelected();
        e.Handled = true;
    }

    private void InsertSelected()
    {
        if (_vm.SelectedHistoryEntry == null) return;
        _vm.InsertHistoryCommand.Execute(null);
        if (_vm.InsertSqlAtCaret != null) Close();
    }
}
