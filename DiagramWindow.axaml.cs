using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
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
    private Point? _lastScrollMousePos;

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

        // Ctrl + mouse wheel zooms the canvas (tunneling so the scroll viewer doesn't scroll first).
        var scroll = this.FindControl<ScrollViewer>("DiagramScroll");
        if (scroll is not null)
        {
            scroll.AddHandler(InputElement.PointerWheelChangedEvent, Scroll_PointerWheelChanged, RoutingStrategies.Tunnel);
            scroll.PointerMoved += (s, e) => _lastScrollMousePos = e.GetPosition(scroll);
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
            _vm.FitToNodesRequested -= OnFitToNodesRequested;
        }

        _vm = DataContext as DiagramViewModel;

        if (_vm is not null)
        {
            _vm.StructureChanged += OnStructureChanged;
            _vm.PositionsChanged += OnPositionsChanged;
            _vm.FocusRequested += OnFocusRequested;
            _vm.FitToNodesRequested += OnFitToNodesRequested;
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

    private void OnFitToNodesRequested(IReadOnlyList<DiagramTableNode> nodes) =>
        Dispatcher.UIThread.Post(() => FitToNodes(nodes));

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
                Opacity = node.IsDimmed ? 0.22 : 1.0,
                ZIndex = node.IsIsolatedTarget ? 25 : (node.IsDimmed ? 1 : 15),
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
            card.Opacity = node.IsDimmed ? 0.22 : 1.0;
            card.ZIndex = node.IsIsolatedTarget ? 25 : (node.IsDimmed ? 1 : 15);
        }

        RedrawLinks();
    }

    private void RedrawLinks()
    {
        var canvas = Canvas;
        if (_vm is null || canvas is null) return;

        // Remove previous link shapes (cards have ZIndex >= 1; links default 0).
        for (var i = canvas.Children.Count - 1; i >= 0; i--)
        {
            if (canvas.Children[i] is not DiagramTableCard)
                canvas.Children.RemoveAt(i);
        }

        if (_vm.Tables.Count == 0 || _vm.Relations.Count == 0) return;
        if (!_vm.ShowLinks) return;

        var nodes = _vm.Tables
            .Where(t => t.IsVisible && _cards.ContainsKey(t.FullName))
            .ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);

        var isolated = _vm.IsolatedTable;
        var stroke = new SolidColorBrush(Color.Parse("#5B5B7A"));
        var dimmedStroke = new SolidColorBrush(Color.Parse("#252538"));
        var highlightStroke = new SolidColorBrush(Color.Parse("#38BDF8"));
        var dotFill = new SolidColorBrush(Color.Parse("#38BDF8"));

        // When isolated, we draw dimmed background links first, then highlighted links on top.
        var (activeRels, otherRels) = isolated is null
            ? (_vm.Relations, (List<DiagramRelation>)[])
            : (_vm.Relations.Where(r => r.ChildTable.Equals(isolated, StringComparison.OrdinalIgnoreCase) ||
                                        r.ParentTable.Equals(isolated, StringComparison.OrdinalIgnoreCase)).ToList(),
               _vm.Relations.Where(r => !r.ChildTable.Equals(isolated, StringComparison.OrdinalIgnoreCase) &&
                                        !r.ParentTable.Equals(isolated, StringComparison.OrdinalIgnoreCase)).ToList());

        // 1. Draw dimmed links if isolated
        if (isolated is not null)
        {
            foreach (var rel in otherRels)
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
                    Stroke = dimmedStroke,
                    StrokeThickness = 1.0,
                    Opacity = 0.35,
                    ZIndex = 0,
                };
                ToolTip.SetTip(line, rel.Label);
                canvas.Children.Add(line);
            }
        }

        // 2. Draw active prominent links
        foreach (var rel in activeRels)
        {
            if (!nodes.TryGetValue(rel.ChildTable, out var child)) continue;
            if (!nodes.TryGetValue(rel.ParentTable, out var parent)) continue;

            var highlighted = isolated is not null;

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
                Stroke = highlighted ? highlightStroke : stroke,
                StrokeThickness = highlighted ? 2.8 : 1.5,
                ZIndex = highlighted ? 8 : 2,
            };
            ToolTip.SetTip(line, rel.Label);
            canvas.Children.Add(line);

            foreach (var p in new[] { start, end })
            {
                var dot = new Ellipse
                {
                    Width = highlighted ? 7 : 6,
                    Height = highlighted ? 7 : 6,
                    Fill = dotFill,
                    ZIndex = highlighted ? 9 : 3,
                };
                Canvas.SetLeft(dot, p.X - (highlighted ? 3.5 : 3));
                Canvas.SetTop(dot, p.Y - (highlighted ? 3.5 : 3));
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

    private void FitToNodes(IReadOnlyList<DiagramTableNode> nodes)
    {
        var scroll = Scroll;
        if (_vm is null || scroll is null || nodes.Count == 0) return;

        var minX = nodes.Min(n => n.X);
        var minY = nodes.Min(n => n.Y);
        var maxX = nodes.Max(n => n.X + n.NodeWidth);
        var maxY = nodes.Max(n => n.Y + n.NodeHeight);

        const double padding = 80.0;
        var totalW = (maxX - minX) + padding * 2;
        var totalH = (maxY - minY) + padding * 2;

        var viewW = scroll.Viewport.Width > 0 ? scroll.Viewport.Width : scroll.Bounds.Width;
        var viewH = scroll.Viewport.Height > 0 ? scroll.Viewport.Height : scroll.Bounds.Height;
        if (viewW <= 0 || viewH <= 0) return;

        var zoomX = viewW / totalW;
        var zoomY = viewH / totalH;
        var targetZoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.1, 1.25);

        _vm.ZoomLevel = Math.Round(targetZoom, 2);

        // Center on the cluster
        Dispatcher.UIThread.Post(() =>
        {
            var curZoom = _vm.ZoomLevel;
            var curViewW = scroll.Viewport.Width > 0 ? scroll.Viewport.Width : scroll.Bounds.Width;
            var curViewH = scroll.Viewport.Height > 0 ? scroll.Viewport.Height : scroll.Bounds.Height;

            var centerX = (minX + maxX) / 2.0;
            var centerY = (minY + maxY) / 2.0;

            var targetX = centerX * curZoom - curViewW / 2.0;
            var targetY = centerY * curZoom - curViewH / 2.0;

            scroll.Offset = new Vector(Math.Max(0, targetX), Math.Max(0, targetY));
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Single entry point for every zoom change. Keeps the canvas point under
    /// <paramref name="viewportAnchor"/> (cursor, or viewport center) fixed on
    /// screen, so zoomed content never jumps out of view.
    /// </summary>
    private void ZoomAround(Point? viewportAnchor, double newZoom)
    {
        var scroll = Scroll;
        if (_vm is null || scroll is null) return;
        newZoom = Math.Clamp(Math.Round(newZoom, 2), 0.1, 2.0);
        var oldZoom = _vm.ZoomLevel;
        if (newZoom == oldZoom) return;

        var anchor = viewportAnchor ?? new Point(
            scroll.Viewport.Width > 0 ? scroll.Viewport.Width / 2 : scroll.Bounds.Width / 2,
            scroll.Viewport.Height > 0 ? scroll.Viewport.Height / 2 : scroll.Bounds.Height / 2);
        var contentX = (scroll.Offset.X + anchor.X) / oldZoom;
        var contentY = (scroll.Offset.Y + anchor.Y) / oldZoom;

        _vm.ZoomLevel = newZoom;

        // Re-anchor after layout catches up with the new zoom.
        Dispatcher.UIThread.Post(() =>
        {
            scroll.Offset = new Vector(
                Math.Max(0, contentX * newZoom - anchor.X),
                Math.Max(0, contentY * newZoom - anchor.Y));
        }, DispatcherPriority.Background);
    }

    private void ZoomIn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is not null) ZoomAround(ScrollMouseAnchor(), _vm.ZoomLevel + 0.1);
    }

    private void ZoomOut_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is not null) ZoomAround(ScrollMouseAnchor(), _vm.ZoomLevel - 0.1);
    }

    private void ResetZoom_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is not null) ZoomAround(ScrollMouseAnchor(), 1.0);
    }

    /// <summary>
    /// Anchor for button zooming: the live mouse position when the pointer is over
    /// the canvas, otherwise null (falls back to viewport center).
    /// </summary>
    private Point? ScrollMouseAnchor()
    {
        var scroll = Scroll;
        if (scroll is null || !_lastScrollMousePos.HasValue || !scroll.IsPointerOver) return null;
        var p = _lastScrollMousePos.Value;
        return new Point(
            Math.Clamp(p.X, 0, Math.Max(0, scroll.Viewport.Width)),
            Math.Clamp(p.Y, 0, Math.Max(0, scroll.Viewport.Height)));
    }

    /// <summary>
    /// Ctrl + wheel zooms toward the mouse pointer; Shift + wheel scrolls horizontally;
    /// plain wheel keeps its normal vertical scroll behavior.
    /// </summary>
    private void Scroll_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var scroll = Scroll;
        if (_vm is null || scroll is null) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            var oldZoom = _vm.ZoomLevel;
            var newZoom = e.Delta.Y > 0 ? oldZoom + 0.1 : oldZoom - 0.1;
            ZoomAround(e.GetPosition(scroll), newZoom);
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Delta.Y != 0)
        {
            e.Handled = true;
            scroll.Offset = new Vector(
                Math.Max(0, scroll.Offset.X - e.Delta.Y * 60),
                scroll.Offset.Y);
        }
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
            AppLog.Warn($"[DiagramWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
            if (DataContext is DiagramViewModel vm)
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
        }
    }
}
