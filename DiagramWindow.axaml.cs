using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SchemaCompare.Controls;
using SchemaCompare.Models;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare;

public partial class DiagramWindow : Window
{
    private DiagramViewModel? _vm;
    private readonly Dictionary<string, DiagramTableCard> _cards = new(StringComparer.OrdinalIgnoreCase);

    private bool _panning;
    private Point _panStart;
    private Vector _scrollStart;

    public DiagramWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        var canvas = this.FindControl<Canvas>("DiagramCanvas");
        if (canvas is not null)
        {
            canvas.PointerPressed += Canvas_PointerPressed;
            canvas.PointerMoved += Canvas_PointerMoved;
            canvas.PointerReleased += Canvas_PointerReleased;
        }

        ClipboardGuard.Attach(this, message =>
        {
            if (DataContext is DiagramViewModel vm)
                vm.StatusMessage = message;
        });
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.StructureChanged -= OnStructureChanged;
            _vm.PositionsChanged -= OnPositionsChanged;
            _vm.FocusRequested -= OnFocusRequested;
        }

        _vm = DataContext as DiagramViewModel;

        if (_vm is not null)
        {
            _vm.StructureChanged += OnStructureChanged;
            _vm.PositionsChanged += OnPositionsChanged;
            _vm.FocusRequested += OnFocusRequested;
            _vm.PickSaveFileAsync = PickSaveFileAsync;
            _vm.PickOpenFileAsync = PickOpenFileAsync;
            _vm.CopyToClipboardAsync = CopyToClipboardAsync;
        }

        RebuildDiagram();
    }

    private void OnStructureChanged() =>
        Dispatcher.UIThread.Post(RebuildDiagram);

    private void OnPositionsChanged() =>
        Dispatcher.UIThread.Post(RefreshNodeVisuals);

    private void OnFocusRequested(DiagramTableNode node) =>
        Dispatcher.UIThread.Post(() => FocusNode(node));

    private Canvas? Canvas => this.FindControl<Canvas>("DiagramCanvas");
    private ScrollViewer? Scroll => this.FindControl<ScrollViewer>("DiagramScroll");

    private void RebuildDiagram()
    {
        var canvas = Canvas;
        if (_vm is null || canvas is null) return;

        canvas.Children.Clear();
        _cards.Clear();

        foreach (var node in _vm.Tables)
        {
            if (!node.IsVisible) continue;
            var card = new DiagramTableCard
            {
                DataContext = node,
                Opacity = node.IsDimmed ? 0.3 : 1.0,
                ZIndex = 10,
            };
            Canvas.SetLeft(card, node.X);
            Canvas.SetTop(card, node.Y);
            canvas.Children.Add(card);
            _cards[node.FullName] = card;
        }

        RedrawLinks();
    }

    private void RefreshNodeVisuals()
    {
        var canvas = Canvas;
        if (_vm is null || canvas is null) return;

        foreach (var node in _vm.Tables)
        {
            if (!_cards.TryGetValue(node.FullName, out var card)) continue;
            Canvas.SetLeft(card, node.X);
            Canvas.SetTop(card, node.Y);
            card.Opacity = node.IsDimmed ? 0.3 : 1.0;
        }

        RedrawLinks();
    }

    private void RedrawLinks()
    {
        var canvas = Canvas;
        if (_vm is null || canvas is null) return;

        // Remove previous link shapes (cards have ZIndex 10; links default 0).
        for (var i = canvas.Children.Count - 1; i >= 0; i--)
        {
            if (canvas.Children[i] is not DiagramTableCard)
                canvas.Children.RemoveAt(i);
        }

        if (_vm.Tables.Count == 0 || _vm.Relations.Count == 0) return;

        var nodes = _vm.Tables
            .Where(t => t.IsVisible && _cards.ContainsKey(t.FullName))
            .ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);

        var stroke = new SolidColorBrush(Color.Parse("#5B5B7A"));
        var dotFill = new SolidColorBrush(Color.Parse("#7DD3FC"));

        foreach (var rel in _vm.Relations)
        {
            if (!nodes.TryGetValue(rel.ChildTable, out var child)) continue;
            if (!nodes.TryGetValue(rel.ParentTable, out var parent)) continue;

            var start = new Point(child.X, child.Y + child.NodeHeight / 2);
            var end = new Point(parent.X + parent.NodeWidth, parent.Y + parent.NodeHeight / 2);
            var midX = (start.X + end.X) / 2;

            var line = new Polyline
            {
                Points = new Avalonia.Collections.AvaloniaList<Point>(
                [
                    start,
                    new Point(midX, start.Y),
                    new Point(midX, end.Y),
                    end,
                ]),
                Stroke = stroke,
                StrokeThickness = 1.5,
            };
            ToolTip.SetTip(line, rel.Label);
            canvas.Children.Add(line);

            foreach (var p in new[] { start, end })
            {
                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = dotFill,
                };
                Canvas.SetLeft(dot, p.X - 3);
                Canvas.SetTop(dot, p.Y - 3);
                ToolTip.SetTip(dot, rel.Label);
                canvas.Children.Add(dot);
            }
        }
    }

    private void FocusNode(DiagramTableNode node)
    {
        var scroll = Scroll;
        if (_vm is null || scroll is null) return;

        var zoom = _vm.ZoomLevel;
        var targetX = node.X * zoom + node.NodeWidth * zoom / 2 - scroll.Viewport.Width / 2;
        var targetY = node.Y * zoom + node.NodeHeight * zoom / 2 - scroll.Viewport.Height / 2;
        scroll.Offset = new Vector(Math.Max(0, targetX), Math.Max(0, targetY));
    }

    // Dragging the empty canvas background pans the scroll viewer.
    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var scroll = Scroll;
        if (scroll is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (!ReferenceEquals(e.Source, sender)) return; // a table card handles its own drag
        _panning = true;
        _panStart = e.GetPosition(scroll);
        _scrollStart = scroll.Offset;
        e.Pointer.Capture((Canvas)sender!);
        e.Handled = true;
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        var scroll = Scroll;
        if (!_panning || scroll is null) return;
        var current = e.GetPosition(scroll);
        scroll.Offset = _scrollStart - (current - _panStart);
        e.Handled = true;
    }

    private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private async Task<string?> PickSaveFileAsync(string suggestedFileName, string extension)
    {
        if (StorageProvider is null) return null;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save diagram layout",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = [new FilePickerFileType("Diagram layout") { Patterns = ["*.diagram.json"] }],
        });
        return file?.Path.LocalPath;
    }

    private async Task<string?> PickOpenFileAsync()
    {
        if (StorageProvider is null) return null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open diagram layout",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Diagram layout") { Patterns = ["*.diagram.json"] }],
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task CopyToClipboardAsync(string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                await ClipboardExtensions.SetTextAsync(cb, text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiagramWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
            if (DataContext is DiagramViewModel vm)
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
        }
    }
}
