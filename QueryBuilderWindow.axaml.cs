using Avalonia.Controls;
using Avalonia.Interactivity;
using SchemaCompare.Models;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare;

/// <summary>
/// Modal dialog window for the visual Query Constructor. The view-model does
/// all the work; this file handles actions and ComboBox selection sync.
/// </summary>
public partial class QueryBuilderWindow : Window
{
    private QueryBuilderViewModel? Vm => DataContext as QueryBuilderViewModel;

    public QueryBuilderWindow()
    {
        InitializeComponent();
    }

    public QueryBuilderWindow(QueryBuilderViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void OnTableSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.DataContext is BuilderTable table && cb.SelectedItem is QuerySchemaService.TableInfo info)
        {
            Vm?.SetTable(table, info);
        }
    }

    private void OnJoinTableSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.DataContext is JoinClause join && cb.SelectedItem is QuerySchemaService.TableInfo info)
        {
            Vm?.SetJoinTable(join, info);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnApplyToActiveClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) { Close(); return; }
        var (target, _) = Vm.Apply(QueryBuilderViewModel.ApplyTarget.ActiveTab);
        if (target != QueryBuilderViewModel.ApplyTarget.Cancelled)
            Close();
    }

    private void OnApplyToNewClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) { Close(); return; }
        var (target, _) = Vm.Apply(QueryBuilderViewModel.ApplyTarget.NewTab);
        if (target != QueryBuilderViewModel.ApplyTarget.Cancelled)
            Close();
    }
}
