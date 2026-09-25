using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using SchemaCompare.Models;

namespace SchemaCompare.Controls;

/// <summary>
/// Draws an execution plan as a top-down SSMS-style operator diagram: each
/// <see cref="PlanNode"/> becomes a box (operator, object, estimated rows, cost %)
/// connected by elbow lines. Clicking a box opens a details strip under the
/// diagram (costs, I/O/CPU split, predicates, warnings); missing-index hints
/// appear as a banner with a ready CREATE INDEX script. Scrollable both ways.
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

    private const double BoxWidth = 178;
    private const double BoxHeight = 60;
    private const double HGap = 26;
    private const double VGap = 62;
    private const double Edge = 16;

    private PlanNode? _selected;
    private ScrollViewer? _scroll;
    private Border? _detailsPanel;
    private Border? _missingIndexPanel;

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

    private void Rebuild()
    {
        var details = _detailsPanel;
        var banner = _missingIndexPanel;
        var scroll = _scroll;
        if (details == null || banner == null || scroll == null) return;

        var canvas = new Canvas();
        scroll.Content = canvas;

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

        var y = Edge;
        var nextLeaf = 0;
        foreach (var stmt in Plan.Statements)
        {
            var header = new TextBlock
            {
                Text = $"▸ {stmt.StatementType}:  {stmt.StatementText}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = ResourceBrush("Primary500", "#7C6FF0"),
                Margin = new Thickness(Edge, y, Edge, 6),
                MaxWidth = 1100,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTip.SetTip(header, $"{stmt.StatementText}\n\n" +
                $"Optimization: {stmt.OptimizationLevel ?? "?"}\n" +
                $"Degree of parallelism: {(stmt.DegreeOfParallelism == 0 ? "serial" : stmt.DegreeOfParallelism?.ToString() ?? "?")}\n" +
                (stmt.MemoryGrantKb is { } mg ? $"Memory grant: {mg:N0} KB\n" : "") +
                (stmt.CachedPlanSizeKb is { } cs ? $"Cached plan size: {cs:N0} KB\n" : "") +
                $"Statement cost: {stmt.SubtreeCost:0.######}");
            Canvas.SetTop(header, y);
            Canvas.SetLeft(header, Edge);
            canvas.Children.Add(header);
            y += 24;

            if (stmt.Root == null)
            {
                y += 12;
                continue;
            }

            var positions = new Dictionary<PlanNode, Point>();
            var maxDepth = 0;
            AssignPositions(stmt.Root, 0, ref nextLeaf, ref maxDepth, positions, y);
            foreach (var (node, pos) in positions)
                DrawNode(canvas, node, pos);
            foreach (var (node, pos) in positions)
                DrawConnectors(canvas, node, positions);

            y = maxDepth * (BoxHeight + VGap) + y + BoxHeight + 34;
        }

        // Legend.
        var legend = new TextBlock
        {
            FontSize = 10,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Edge, 4, Edge, 12),
            Text = "Cost % = operator's share of the total estimated plan cost (box color: green cheap → red expensive). ⚠ = optimizer warning. Click any box for full details. Rows are optimizer estimates, not actual counts.",
            Foreground = ResourceBrush("OnSurfaceVariant", "#98A0B3")
        };
        Canvas.SetTop(legend, y);
        Canvas.SetLeft(legend, Edge);
        canvas.Children.Add(legend);
        y += 42;

        canvas.Width = Math.Max(nextLeaf * (BoxWidth + HGap) - HGap + Edge * 2, 500);
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

    private void AssignPositions(PlanNode node, int depth, ref int nextLeaf, ref int maxDepth,
        Dictionary<PlanNode, Point> positions, double top)
    {
        if (depth > maxDepth) maxDepth = depth;
        double x;
        if (node.Children.Count == 0)
        {
            x = Edge + nextLeaf * (BoxWidth + HGap) + BoxWidth / 2;
            nextLeaf++;
        }
        else
        {
            foreach (var child in node.Children)
                AssignPositions(child, depth + 1, ref nextLeaf, ref maxDepth, positions, top);
            x = (positions[node.Children[0]].X + positions[node.Children[^1]].X) / 2;
        }
        positions[node] = new Point(x, top + depth * (BoxHeight + VGap));
    }

    private void DrawConnectors(Canvas canvas, PlanNode node, Dictionary<PlanNode, Point> positions)
    {
        var parent = positions[node];
        foreach (var child in node.Children)
        {
            if (!positions.TryGetValue(child, out var pos)) continue;
            var midY = parent.Y + BoxHeight + VGap / 2;
            var points = new List<Point>
            {
                new(parent.X, parent.Y + BoxHeight),
                new(parent.X, midY),
                new(pos.X, midY),
                new(pos.X, pos.Y)
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
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 12,
                        FontWeight = FontWeight.Bold,
                        Foreground = brush.Text,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
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
                        Text = $"≈ {ExecutionPlan.FormatRows(node.EstimatedRows)} rows   Cost {node.CostPercent:0.#}%",
                        FontSize = 10.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = brush.Text,
                        Opacity = 0.95
                    }
                }
            }
        };
        Canvas.SetLeft(border, pos.X - BoxWidth / 2);
        Canvas.SetTop(border, pos.Y);
        ToolTip.SetTip(border, BuildTooltip(node));
        border.PointerPressed += (_, _) =>
        {
            _selected = ReferenceEquals(node, _selected) ? null : node;
            Rebuild();
        };
        canvas.Children.Add(border);
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
            ("Operator", $"{node.PhysicalOp}  (logical: {node.LogicalOp})"),
        };
        if (!string.IsNullOrEmpty(node.ObjectName)) rows.Add(("Object", node.ObjectName));
        if (!string.IsNullOrEmpty(node.Predicate)) rows.Add(("Predicate", node.Predicate));
        if (!string.IsNullOrEmpty(node.SortOrder)) rows.Add(("Sort output", node.SortOrder));
        rows.Add(("Estimated rows", ExecutionPlan.FormatRows(node.EstimatedRows)));
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
