using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

        var scroll = this.FindControl<ScrollViewer>("DiagramScroll");
        if (scroll is not null)
            scroll.PointerWheelChanged += Scroll_PointerWheelChanged;

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
            _vm.ExportPngRequested -= OnExportPngRequested;
        }

        _vm = DataContext as DiagramViewModel;

        if (_vm is not null)
        {
            _vm.StructureChanged += OnStructureChanged;
            _vm.PositionsChanged += OnPositionsChanged;
            _vm.FocusRequested += OnFocusRequested;
            _vm.ExportPngRequested += OnExportPngRequested;
            _vm.PickSaveFileAsync = PickSaveFileAsync;
            _vm.PickOpenFileAsync = PickOpenFileAsync;
            _vm.PickPngFileAsync = PickPngFileAsync;
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
        var highlightStroke = new SolidColorBrush(Color.Parse("#7DD3FC"));
        var isolated = _vm.IsolatedTable;

        foreach (var rel in _vm.Relations)
        {
            if (!nodes.TryGetValue(rel.ChildTable, out var child)) continue;
            if (!nodes.TryGetValue(rel.ParentTable, out var parent)) continue;

            // Isolation mode: draw only edges touching the isolated table, highlighted.
            bool touchesIsolated = isolated is null ||
                string.Equals(rel.ChildTable, isolated.FullName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rel.ParentTable, isolated.FullName, StringComparison.OrdinalIgnoreCase);
            if (!touchesIsolated) continue;
            var isHighlight = isolated is not null;

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
                Stroke = isHighlight ? highlightStroke : stroke,
                StrokeThickness = isHighlight ? 2.5 : 1.5,
            };
            ToolTip.SetTip(line, rel.Label);
            canvas.Children.Add(line);

            foreach (var p in new[] { start, end })
            {
                var dot = new Ellipse
                {
                    Width = isHighlight ? 8 : 6,
                    Height = isHighlight ? 8 : 6,
                    Fill = dotFill,
                };
                Canvas.SetLeft(dot, p.X - dot.Width / 2);
                Canvas.SetTop(dot, p.Y - dot.Height / 2);
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

    // Ctrl+mouse-wheel zooms anchored at the cursor; plain wheel keeps scrolling.
    private void Scroll_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var scroll = Scroll;
        if (_vm is null || scroll is null) return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        var oldZoom = _vm.ZoomLevel;
        var step = e.Delta.Y > 0 ? 0.1 : -0.1;
        var newZoom = Math.Max(0.25, Math.Min(2.5, Math.Round(oldZoom + step, 2)));
        if (Math.Abs(newZoom - oldZoom) < 0.001) { e.Handled = true; return; }

        // Keep the content point under the cursor stable across the zoom.
        var cursor = e.GetPosition(scroll);
        var contentX = (scroll.Offset.X + cursor.X) / oldZoom;
        var contentY = (scroll.Offset.Y + cursor.Y) / oldZoom;
        _vm.ApplyZoom(newZoom);
        scroll.Offset = new Vector(
            Math.Max(0, contentX * newZoom - cursor.X),
            Math.Max(0, contentY * newZoom - cursor.Y));
        e.Handled = true;
    }

    private Task OnExportPngRequested() => ExportDiagramPngAsync();

    private async Task ExportDiagramPngAsync()
    {
        var canvas = Canvas;
        if (_vm is null || canvas is null || _vm.PickPngFileAsync is null) return;
        if (!_vm.HasSchema || _vm.Tables.Count == 0)
        {
            _vm.ErrorMessage = "Load a schema before exporting PNG.";
            return;
        }
        var path = await _vm.PickPngFileAsync(_vm.SuggestedPngFileName);
        if (path is null) return;
        try
        {
            var width = (int)Math.Min(8000, Math.Max(200, _vm.CanvasWidth));
            var height = (int)Math.Min(8000, Math.Max(200, _vm.CanvasHeight));
            // Ensure layout is current before rendering.
            canvas.Measure(new Size(width, height));
            canvas.Arrange(new Rect(0, 0, width, height));
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            bitmap.Render(canvas);
#pragma warning disable CS0618 // Save(string) works; new encoder overload is optional.
            bitmap.Save(path);
#pragma warning restore CS0618
            _vm.ShowError = false;
            _vm.StatusMessage = $"Diagram exported to PNG: {path} ({width}×{height}).";
        }
        catch (Exception ex)
        {
            _vm.ErrorMessage = $"PNG export failed: {ex.Message}";
            _vm.ShowError = true;
        }
    }

    private async Task<string?> PickPngFileAsync(string suggestedFileName)
    {
        if (StorageProvider is null) return null;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export diagram as PNG",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });
        return file?.Path.LocalPath;
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
