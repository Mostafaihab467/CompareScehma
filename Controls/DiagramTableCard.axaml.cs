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
/// Header icons: 🔗 isolates this table's relations, ⌖ centers it in view.
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

        var isolate = this.FindControl<Button>("IsolateButton");
        if (isolate is not null)
            isolate.Click += (_, _) => ResolveVm()?.IsolateTableCommand.Execute(DataContext as DiagramTableNode);
        var focus = this.FindControl<Button>("FocusButton");
        if (focus is not null)
            focus.Click += (_, _) => ResolveVm()?.FocusTableCommand.Execute(DataContext as DiagramTableNode);
    }

    private DiagramViewModel? ResolveVm() =>
        this.FindAncestorOfType<Window>()?.DataContext as DiagramViewModel;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not DiagramTableNode) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Header icon buttons handle their own clicks — don't start a drag from them.
        if (e.Source is Button || (e.Source as Control)?.FindAncestorOfType<Button>() is not null) return;
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
