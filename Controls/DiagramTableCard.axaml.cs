using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SchemaCompare.Models;
using SchemaCompare.ViewModels;

namespace SchemaCompare.Controls;

/// <summary>
/// A draggable ER table card. Dragging anywhere on the card moves the bound
/// <see cref="DiagramTableNode"/> (X/Y), which the diagram window observes to
/// redraw relationship links and auto-save the layout.
/// </summary>
public partial class DiagramTableCard : UserControl
{
    private bool _dragging;
    private Point _grabOffset;

    public DiagramTableCard()
    {
        InitializeComponent();
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;

        var relationsButton = this.FindControl<Button>("RelationsButton");
        if (relationsButton is not null)
            relationsButton.Click += OnRelationsButtonClick;

        DoubleTapped += (s, e) =>
        {
            if (DataContext is not DiagramTableNode node) return;
            if (this.FindAncestorOfType<Window>()?.DataContext is DiagramViewModel vm &&
                vm.IsolateRelationsCommand.CanExecute(node))
                vm.IsolateRelationsCommand.Execute(node);
            e.Handled = true;
        };
    }

    /// <summary>
    /// The 🔗 header button isolates this table's relations. It resolves the
    /// diagram view-model from the hosting window (the card's DataContext is
    /// the node itself, so a view-model command binding is not available here).
    /// </summary>
    private void OnRelationsButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not DiagramTableNode node) return;
        if (this.FindAncestorOfType<Window>()?.DataContext is DiagramViewModel vm &&
            vm.IsolateRelationsCommand.CanExecute(node))
            vm.IsolateRelationsCommand.Execute(node);
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not DiagramTableNode) return;
        if (e.Source is Button) return; // let header buttons (🔗) handle their own click
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _grabOffset = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || DataContext is not DiagramTableNode node) return;
        var canvas = this.FindAncestorOfType<Canvas>();
        if (canvas is null) return;
        var pos = e.GetPosition(canvas);
        node.X = Math.Max(0, pos.X - _grabOffset.X);
        node.Y = Math.Max(0, pos.Y - _grabOffset.Y);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}
