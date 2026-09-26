using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SchemaCompare.Models;

namespace SchemaCompare.Controls;

/// <summary>
/// Draws an execution plan as a left-to-right SSMS-style operator diagram: each
/// <see cref="PlanNode"/> becomes an icon-bearing box (operator, object, estimated
/// rows, cost %) connected by elbow lines, statements stacked in vertical bands.
/// Clicking a box opens a details strip under the diagram; missing-index hints
/// appear as a banner with a ready CREATE INDEX script. Ctrl + wheel zooms.
/// </summary>
public sealed class PlanDiagramControl : ContentControl
{
    public static readonly StyledProperty<ExecutionPlan?> PlanProperty =
        AvaloniaProperty.Register<PlanDiagramControl, ExecutionPlan?>(nameof(Plan));

    public ExecutionPlan? Plan
    {
        get => GetValue(PlanProperty);
        set => SetValue(PlanProperty, value);
    }

    private const double BoxWidth = 186;
    private const double BoxHeight = 62;
    /// <summary>Gap between depth columns (left to right).</summary>
    private const double HGap = 48;
    /// <summary>Gap between sibling rows.</summary>
    private const double VGap = 20;
    private const double Edge = 16;
    private const double MinZoom = 0.4;
    private const double MaxZoom = 2.5;

    private PlanNode? _selected;
    private ScrollViewer? _scroll;
    private LayoutTransformControl? _zoomHost;
    private Border? _detailsPanel;
    private Border? _missingIndexPanel;
    private Point? _pointerPos;
    private double _zoom = 1.0;

    public PlanDiagramControl()
    {
        _missingIndexPanel = BuildMissingIndexPanel();
        _detailsPanel = BuildDetailsPanel();
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(0)
        };
        _zoomHost = new LayoutTransformControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            LayoutTransform = new ScaleTransform(_zoom, _zoom)
        };
        _scroll.Content = _zoomHost;

        // Tunnel so Ctrl+wheel is consumed before the ScrollViewer scrolls the canvas.
        _scroll.AddHandler(InputElement.PointerWheelChangedEvent,
            new EventHandler<PointerWheelEventArgs>(OnPointerWheelChanged), RoutingStrategies.Tunnel);
        _scroll.PointerMoved += (_, e) => _pointerPos = e.GetPosition(_scroll);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(_missingIndexPanel, 0);
        Grid.SetRow(_scroll, 1);
        Grid.SetRow(_detailsPanel, 2);
        grid.Children.Add(_missingIndexPanel);
        grid.Children.Add(_scroll);
        grid.Children.Add(_detailsPanel);
        Content = grid;
        AttachedToVisualTree += (_, _) => Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlanProperty)
        {
            _selected = null;
            _zoom = 1.0;
            if (_zoomHost != null) _zoomHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
            Rebuild();
        }
    }

    private static Border BuildMissingIndexPanel()
    {
        return new Border
        {
            Background = Solid("#1E3D2A"),
            BorderBrush = Solid("#3FA96B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6),
            Margin = new Thickness(8, 6, 8, 0),
            IsVisible = false
        };
    }

    private static Border BuildDetailsPanel()
    {
        return new Border
        {
            Background = Solid("#1A1D27"),
            BorderBrush = Solid("#3A3F4F"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8),
            MaxHeight = 190,
            IsVisible = false
        };
    }

    // ─── Zoom ───────────────────────────────────────────────────────────────

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_scroll == null || e.Delta.Y == 0) return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        e.Handled = true;
        // Anchor on the pointer when known so zoomed content stays under the cursor.
        var anchor = _pointerPos ?? e.GetPosition(_scroll);
        ZoomAt(anchor, _zoom * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12));
    }

    private void ZoomAt(Point? viewportAnchor, double newZoom)
    {
        var scroll = _scroll;
        var host = _zoomHost;
        if (scroll == null || host == null) return;

        newZoom = Math.Clamp(Math.Round(newZoom, 3), MinZoom, MaxZoom);
        var oldZoom = _zoom;
        if (newZoom == oldZoom) return;

        var anchor = viewportAnchor ?? new Point(
            scroll.Viewport.Width > 0 ? scroll.Viewport.Width / 2 : scroll.Bounds.Width / 2,
            scroll.Viewport.Height > 0 ? scroll.Viewport.Height / 2 : scroll.Bounds.Height / 2);
        var contentX = (scroll.Offset.X + anchor.X) / oldZoom;
        var contentY = (scroll.Offset.Y + anchor.Y) / oldZoom;

        _zoom = newZoom;
        host.LayoutTransform = new ScaleTransform(newZoom, newZoom);

        // Re-anchor once layout has caught up with the new scale.
        Dispatcher.UIThread.Post(() =>
        {
            scroll.Offset = new Vector(
                Math.Max(0, contentX * newZoom - anchor.X),
                Math.Max(0, contentY * newZoom - anchor.Y));
        }, DispatcherPriority.Background);
    }

    // ─── Layout ──────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        var details = _detailsPanel;
        var banner = _missingIndexPanel;
        var scroll = _scroll;
        var host = _zoomHost;
        if (details == null || banner == null || scroll == null || host == null) return;

        var canvas = new Canvas();
        host.Child = canvas;

        if (Plan == null || Plan.Statements.Count == 0)
        {
            details.IsVisible = false;
            banner.IsVisible = false;
            canvas.Children.Add(new TextBlock
            {
                Text = "No plan captured. Turn on 🧭 Plan in the toolbar, then execute.",
                Margin = new Thickness(12),
                Opacity = 0.7,
                FontSize = 12
            });
            canvas.Width = 500;
            canvas.Height = 60;
            return;
        }

        // Missing-index banner (first suggestion wins).
        var suggestion = Plan.Statements.Select(s => s.MissingIndex).FirstOrDefault(m => m != null);
        if (suggestion != null)
        {
            banner.Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = $"⚠ Missing index — the optimizer estimates this index could improve the query by ~{suggestion.Impact:0.#}% ({suggestion.Table}):",
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Solid("#9FE8BC"),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = suggestion.CreateScript,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11.5,
                        Foreground = Solid("#CFEEDC"),
                        Margin = new Thickness(0, 4, 0, 0)
                    }
                }
            };
            banner.IsVisible = true;
        }
        else
        {
            banner.IsVisible = false;
        }

        // Horizontal tree: depth runs left→right, siblings stack top→bottom, and
        // each statement gets its own vertical band.
        var y = Edge;
        // The one line that tells the two buttons apart: an estimated plan and an actual one
        // draw the same boxes, and reading a guess as a measurement is the whole mistake.
        var measured = Plan.HasRuntimeStats;
        var kindLine = new TextBlock
        {
            Text = measured
                ? "ACTUAL plan — rows, time and reads below were measured while the query ran."
                : "ESTIMATED plan — the query was compiled, not run: every number below is the optimizer's guess.",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Solid(measured ? "#9FE8BC" : "#D0A93F"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        Canvas.SetTop(kindLine, y);
        Canvas.SetLeft(kindLine, Edge);
        canvas.Children.Add(kindLine);
        y += 22;
        var widestBand = 0.0;
        foreach (var stmt in Plan.Statements)
        {
            var header = new TextBlock
            {
                Text = $"▸ {stmt.StatementType}:  {stmt.StatementText}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = ResourceBrush("Primary500", "#7C6FF0"),
                MaxWidth = 1100,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTip.SetTip(header, $"{stmt.StatementText}\n\n" +
                $"Optimization: {stmt.OptimizationLevel ?? "?"}\n" +
                $"Degree of parallelism: {(stmt.DegreeOfParallelism == 0 ? "serial" : stmt.DegreeOfParallelism?.ToString() ?? "?")}\n" +
                (stmt.MemoryGrantKb is { } mg ? $"Memory grant: {mg:N0} KB\n" : "") +
                (stmt.CachedPlanSizeKb is { } cs ? $"Cached plan size: {cs:N0} KB\n" : "") +
                $"Statement cost: {stmt.SubtreeCost:0.######}" +
                (stmt.ActualElapsedMs is { } elapsedMs
                    ? $"\nMeasured by the server: {elapsedMs:N0} ms elapsed, {stmt.ActualCpuMs ?? 0:N0} ms CPU"
                    : ""));
            Canvas.SetTop(header, y);
            Canvas.SetLeft(header, Edge);
            canvas.Children.Add(header);
            y += 24;

            if (stmt.Root == null)
            {
                y += 12;
                continue;
            }

            var top = y;
            var nextLeaf = 0;
            var maxDepth = 0;
            var positions = new Dictionary<PlanNode, Point>();
            AssignPositions(stmt.Root, 0, ref nextLeaf, ref maxDepth, positions, Edge, top);
            foreach (var (node, pos) in positions)
                DrawNode(canvas, node, pos);
            foreach (var (node, pos) in positions)
                DrawConnectors(canvas, node, positions);

            widestBand = Math.Max(widestBand, (maxDepth + 1) * (BoxWidth + HGap) - HGap + Edge * 2);
            y = top + nextLeaf * (BoxHeight + VGap) - VGap + 30;
        }

        // Legend.
        var legend = new TextBlock
        {
            FontSize = 10,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Edge, 4, Edge, 12),
            Text = "Data flows left → right. Cost % = operator's share of the total estimated plan cost (box color: green cheap → red expensive). ⚠ = optimizer warning. Click any box for full details; Ctrl + scroll to zoom. "
                + (measured
                    ? "Rows read \"estimate → actual\"; ⚠ on a row estimate means the two differ by 10× or more."
                    : "Rows are optimizer estimates, not actual counts — run with 🧭 Plan to measure them."),
            Foreground = ResourceBrush("OnSurfaceVariant", "#98A0B3")
        };
        Canvas.SetTop(legend, y);
        Canvas.SetLeft(legend, Edge);
        canvas.Children.Add(legend);
        y += 42;

        canvas.Width = Math.Max(widestBand, 500);
        canvas.Height = y;

        if (_selected != null && ContainsNode(Plan, _selected))
            ShowDetails(_selected);
        else
        {
            _selected = null;
            details.IsVisible = false;
        }
    }

    private static bool ContainsNode(ExecutionPlan plan, PlanNode node) =>
        plan.Statements.Any(s => s.Root != null && s.Root.SelfAndDescendants().Contains(node));

    /// <summary>Positions are box centers: depth drives X, sibling order drives Y.</summary>
    private void AssignPositions(PlanNode node, int depth, ref int nextLeaf, ref int maxDepth,
        Dictionary<PlanNode, Point> positions, double left, double top)
    {
        if (depth > maxDepth) maxDepth = depth;
        var x = left + depth * (BoxWidth + HGap) + BoxWidth / 2;
        double y;
        if (node.Children.Count == 0)
        {
            y = top + nextLeaf * (BoxHeight + VGap) + BoxHeight / 2;
            nextLeaf++;
        }
        else
        {
            foreach (var child in node.Children)
                AssignPositions(child, depth + 1, ref nextLeaf, ref maxDepth, positions, left, top);
            y = (positions[node.Children[0]].Y + positions[node.Children[^1]].Y) / 2;
        }
        positions[node] = new Point(x, y);
    }

    private void DrawConnectors(Canvas canvas, PlanNode node, Dictionary<PlanNode, Point> positions)
    {
        var parent = positions[node];
        foreach (var child in node.Children)
        {
            if (!positions.TryGetValue(child, out var pos)) continue;
            var midX = parent.X + BoxWidth / 2 + HGap / 2;
            var points = new List<Point>
            {
                new(parent.X + BoxWidth / 2, parent.Y),
                new(midX, parent.Y),
                new(midX, pos.Y),
                new(pos.X - BoxWidth / 2, pos.Y)
            };
            canvas.Children.Add(new Polyline
            {
                Points = points,
                Stroke = new SolidColorBrush(Color.Parse("#8A93A8")),
                StrokeThickness = 1.4
            });
        }
    }

    private void DrawNode(Canvas canvas, PlanNode node, Point pos)
    {
        var isSelected = ReferenceEquals(node, _selected);
        var hasWarnings = node.Warnings.Count > 0;
        var brush = CostBrush(node.CostPercent);
        var borderBrush = isSelected ? Solid("#FFFFFF") : hasWarnings ? Solid("#D0A93F") : brush.Border;
        var title = (node.IsParallel ? "∥ " : "") + (hasWarnings ? "⚠ " : "") + node.Label;

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5
        };
        titleRow.Children.Add(new TextBlock
        {
            Text = OperatorIcon.IconFor(node),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = brush.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var border = new Border
        {
            Width = BoxWidth,
            Height = BoxHeight,
            CornerRadius = new CornerRadius(7),
            Background = brush.Background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(isSelected ? 2.2 : 1.4),
            Padding = new Thickness(8, 5),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    titleRow,
                    new TextBlock
                    {
                        Text = node.ObjectName ?? node.LogicalOp,
                        FontSize = 10,
                        Foreground = brush.Text,
                        Opacity = 0.85,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = RowsLine(node, Plan?.HasRuntimeStats == true),
                        FontSize = 10.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = brush.Text,
                        Opacity = 0.95,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            }
        };
        Canvas.SetLeft(border, pos.X - BoxWidth / 2);
        Canvas.SetTop(border, pos.Y - BoxHeight / 2);
        ToolTip.SetTip(border, BuildTooltip(node));
        border.PointerPressed += (_, _) =>
        {
            _selected = ReferenceEquals(node, _selected) ? null : node;
            Rebuild();
        };
        canvas.Children.Add(border);
    }

    /// <summary>
    /// The box's row line. Before the query runs there is only the optimizer's guess; after it,
    /// the guess and the count that came out of the operator, because the gap between those two
    /// is what makes an actual plan worth looking at.
    /// </summary>
    private static string RowsLine(PlanNode node, bool planMeasured)
    {
        var estimate = $"≈{ExecutionPlan.FormatRows(node.EstimatedRows)}";
        // The box is 186 px wide and this is its longest line: every character here that is not
        // a number costs the one that is.
        var rows = node.ActualRows is { } actual
            ? $"{estimate}→{ExecutionPlan.FormatRows(actual)} rows"
              + (node.Executions > 1 ? $" ×{node.Executions:N0}" : "")
            // An executed plan with no counters for one operator means the query never reached
            // it — say so, or its estimate reads as a measurement like every other box.
            : planMeasured ? estimate + " rows (not run)" : estimate + " rows";
        return $"{rows}  Cost {node.CostPercent:0.#}%";
    }

    private static string BuildTooltip(PlanNode node)
    {
        var rows = node.EstimatedRows == Math.Floor(node.EstimatedRows) && node.EstimatedRows < 1_000
            ? node.EstimatedRows.ToString("0")
            : node.EstimatedRows.ToString("0.###", CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        sb.Append($"{node.PhysicalOp}  (logical: {node.LogicalOp})\n");
        if (node.ObjectName != null) sb.Append($"Object: {node.ObjectName}\n");
        if (node.Predicate != null) sb.Append($"{node.Predicate}\n");
        if (node.SortOrder != null) sb.Append($"Sort output: {node.SortOrder}\n");
        sb.Append($"Estimated rows: {rows}\n");
        if (node.EstimatedRowSize > 0) sb.Append($"Est. row size: {node.EstimatedRowSize:0.#} bytes\n");
        if (node.ActualRows is { } actual)
        {
            sb.Append($"Actual rows: {ExecutionPlan.FormatRows(actual)}");
            if (node.Executions is > 1) sb.Append($" over {node.Executions:N0} executions");
            sb.Append('\n');
            // A scan that read 65,000 rows to hand back 5 is the story the estimate never tells.
            if (node.ActualRowsRead is { } examined && examined > actual)
                sb.Append($"Rows examined: {ExecutionPlan.FormatRows(examined)} to return "
                          + $"{ExecutionPlan.FormatRows(actual)}\n");
            var timing = new List<string>();
            if (node.ActualTimeMs is { } ms) timing.Add($"{ms:0.###} ms elapsed");
            if (node.ActualCpuMs is { } cpu) timing.Add($"{cpu:0.###} ms CPU");
            if (node.ActualLogicalReads is { } logical && logical > 0) timing.Add($"{logical:N0} logical reads");
            if (node.ActualPhysicalReads is { } physical && physical > 0) timing.Add($"{physical:N0} physical reads");
            if (timing.Count > 0) sb.Append("Actual: ").Append(string.Join(", ", timing)).Append('\n');
        }
        sb.Append($"Operator cost: {node.NodeCost:0.######}");
        if (node.EstimatedIo > 0 || node.EstimatedCpu > 0)
            sb.Append($"  (I/O {node.EstimatedIo:0.######}, CPU {node.EstimatedCpu:0.######})");
        sb.Append($"\nSubtree cost: {node.SubtreeCost:0.######}  ({node.CostPercent:0.#}% of plan)");
        if (node.IsParallel) sb.Append("\nParallel execution");
        foreach (var w in node.Warnings)
            sb.Append("\n⚠ ").Append(w);
        return sb.ToString();
    }

    private void ShowDetails(PlanNode node)
    {
        var details = _detailsPanel!;
        var rows = new List<(string Label, string Value)>
        {
            ("Operator", $"{OperatorIcon.IconFor(node)}  {node.PhysicalOp}  (logical: {node.LogicalOp})"),
        };
        if (!string.IsNullOrEmpty(node.ObjectName)) rows.Add(("Object", node.ObjectName));
        if (!string.IsNullOrEmpty(node.Predicate)) rows.Add(("Predicate", node.Predicate));
        if (!string.IsNullOrEmpty(node.SortOrder)) rows.Add(("Sort output", node.SortOrder));
        rows.Add(("Estimated rows", ExecutionPlan.FormatRows(node.EstimatedRows)));
        if (node.ActualRows is { } actualRows)
        {
            rows.Add(("Actual rows", ExecutionPlan.FormatRows(actualRows)
                      + (node.Executions is > 1 ? $" over {node.Executions:N0} executions" : "")));
            if (node.EstimateSkew is { } skew && skew >= ExecutionPlan.SkewWarningFactor)
                rows.Add(("Estimate was off by", $"{skew:0.#}×"));
            if (node.ActualRowsRead is { } examined && examined > actualRows)
                rows.Add(("Rows examined", $"{ExecutionPlan.FormatRows(examined):N0} to return {ExecutionPlan.FormatRows(actualRows)}"));
        }
        if (node.ActualTimeMs is { } timeMs) rows.Add(("Actual elapsed", $"{timeMs:0.###} ms"));
        if (node.ActualCpuMs is { } cpuMs) rows.Add(("Actual CPU", $"{cpuMs:0.###} ms"));
        if (node.ActualLogicalReads is > 0 || node.ActualPhysicalReads is > 0)
            rows.Add(("Actual reads", $"{node.ActualLogicalReads ?? 0:N0} logical, {node.ActualPhysicalReads ?? 0:N0} physical"));
        if (node.EstimatedRowSize > 0) rows.Add(("Est. row size", $"{node.EstimatedRowSize:0.#} bytes"));
        rows.Add(("Cost", $"{node.CostPercent:0.#}% of plan — operator {node.NodeCost:0.######}" +
                          (node.EstimatedIo > 0 || node.EstimatedCpu > 0
                              ? $" (I/O {node.EstimatedIo:0.######}, CPU {node.EstimatedCpu:0.######})"
                              : "") +
                  $", subtree {node.SubtreeCost:0.######}"));
        rows.Add(("Execution", node.IsParallel ? "Parallel" : "Serial"));

        var left = new StackPanel { Spacing = 2 };
        foreach (var (label, value) in rows)
        {
            var line = new TextBlock
            {
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Inlines =
                {
                    new Avalonia.Controls.Documents.Run($"{label}:  ")
                    {
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ResourceBrush("Primary500", "#9C90F2")
                    },
                    new Avalonia.Controls.Documents.Run(value)
                    {
                        Foreground = ResourceBrush("PrimaryText", "#E7E9EE")
                    }
                }
            };
            left.Children.Add(line);
        }

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = "OPERATOR DETAILS  —  click another box to inspect it, click it again to dismiss",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Opacity = 0.6
        });
        panel.Children.Add(left);
        if (node.Warnings.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = Solid("#F3DC9C"),
                TextWrapping = TextWrapping.Wrap,
                Text = "⚠ " + string.Join("   •   ", node.Warnings)
            });
        }
        details.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = panel
        };
        details.IsVisible = true;
    }

    private (IBrush Background, IBrush Border, IBrush Text) CostBrush(double percent) => percent switch
    {
        < 10 => (Solid("#1E3D2A"), Solid("#3FA96B"), Solid("#9FE8BC")),
        < 25 => (Solid("#1E3350"), Solid("#4D8FD6"), Solid("#BAD9F7")),
        < 50 => (Solid("#3E3318"), Solid("#D0A93F"), Solid("#F3DC9C")),
        _    => (Solid("#41221E"), Solid("#D65A4D"), Solid("#F7C1BA"))
    };

    private static SolidColorBrush Solid(string hex) => new(Color.Parse(hex));

    private static IBrush ResourceBrush(string key, string fallback) =>
        Application.Current?.TryGetResource(key, null, out var value) == true && value is IBrush b
            ? b
            : Solid(fallback);
}

/// <summary>
/// One glyph per operator family, SSMS-style. Matching ignores spaces/case so it
/// works on both "Clustered Index Scan" and "ClusteredIndexScan" spellings; the
/// first rule that matches wins, so specific families come before generic ones.
/// </summary>
public static class OperatorIcon
{
    private static readonly (string Fragment, string Icon)[] Rules =
    [
        // DML actions first — the verb matters more than the access path.
        ("bulkinsert", "📥"), ("bcp", "📥"),
        ("insert", "➕"), ("update", "✏️"), ("delete", "🗑"),
        // Index / table access shapes.
        ("columnstore", "📚"),
        ("clustered", "🍇"),
        ("spool", "📦"),
        ("agg", "🧮"),
        ("assert", "⚖️"),
        ("filter", "✅"),
        ("seek", "🔎"),
        ("indexscan", "🗂"),
        ("index", "🗃"),
        ("tablescan", "🧾"), ("table", "🧾"),
        ("constant", "🧊"),
        // Joins.
        ("nestedloops", "🪆"), ("loops", "🪆"),
        ("mergejoin", "🔀"),
        ("hash", "#️⃣"),
        ("join", "🔗"),
        // Flow control.
        ("sort", "🔢"),
        ("top", "🔝"),
        ("segment", "🧩"),
        ("distinct", "🏷"),
        ("window", "🪟"),
        ("scalar", "✳️"),
        // Parallelism / exchange.
        ("gatherstreams", "📡"),
        ("repartition", "🌀"), ("exchange", "🌀"), ("parallelism", "🌀"),
        // Misc.
        ("rowcount", "➖"),
        ("fetch", "🪝"),
        ("remote", "🌐"),
        ("bif", "🔮"), ("fulltext", "🔮"),
        ("select", "📤"),
        ("declare", "💬"), ("stmt", "💬"), ("execute", "💬"),
        ("sequence", "🎞"),
    ];

    public static string IconFor(PlanNode node)
    {
        var op = Normalize(node.PhysicalOp);
        if (op.Length == 0) op = Normalize(node.LogicalOp);
        // "Non Clustered …" must not inherit the clustered glyph.
        op = op.Replace("nonclustered", "");
        foreach (var (fragment, icon) in Rules)
            if (op.Contains(fragment, StringComparison.Ordinal))
                return icon;
        return "⚙️";
    }

    private static string Normalize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
