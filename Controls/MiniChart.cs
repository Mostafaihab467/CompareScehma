using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SchemaCompare.Controls;

public sealed class ChartSeries
{
    public required IReadOnlyList<double> Points { get; init; }
    public required IBrush Stroke { get; init; }
    public string? Label { get; init; }
    /// <summary>Fill the area under the line with a translucent version of the stroke.</summary>
    public bool Fill { get; init; }
}

/// <summary>
/// Minimal live line chart (no external dependency). Draws one or more series
/// stretched across the plot, a translucent area under the first series when
/// requested, horizontal gridlines with a Y-axis max label, and a legend with
/// the latest value per series. Points are ordered oldest to newest.
/// </summary>
public class MiniChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<MiniChart, IReadOnlyList<ChartSeries>?>(nameof(Series));

    public static readonly StyledProperty<double?> MaxYProperty =
        AvaloniaProperty.Register<MiniChart, double?>(nameof(MaxY));

    /// <summary>Appended to legend values, e.g. "%" or " MB".</summary>
    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<MiniChart, string>(nameof(Unit), "");

    public IReadOnlyList<ChartSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public double? MaxY
    {
        get => GetValue(MaxYProperty);
        set => SetValue(MaxYProperty, value);
    }

    public string Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    static MiniChart() => AffectsRender<MiniChart>(SeriesProperty, MaxYProperty, UnitProperty);

    private static readonly Typeface AxisTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface LegendTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);

    private static IBrush AxisBrush() => new SolidColorBrush(Color.Parse("#8A8FA3"));

    public override void Render(DrawingContext context)
    {
        var seriesList = Series;
        var bounds = Bounds;
        var gridBrush = new SolidColorBrush(Color.Parse("#2A2D3A"));
        var textBrush = AxisBrush();

        const double left = 34, top = 4, right = 6;
        const double legendH = 16;
        var plotW = Math.Max(1, bounds.Width - left - right);
        var plotH = Math.Max(1, bounds.Height - top - legendH - 4);

        var maxValue = MaxY ?? ComputeMax(seriesList);
        if (maxValue <= 0) maxValue = 1;

        // Gridlines at 0, 50%, 100% with Y labels.
        for (var i = 0; i <= 2; i++)
        {
            var frac = i / 2.0;
            var y = top + plotH * (1 - frac);
            context.DrawLine(new Pen(gridBrush, 1, dashStyle: DashStyle.Dash),
                new Point(left, y), new Point(left + plotW, y));
            var label = FormatValue(maxValue * frac);
            DrawText(context, label, AxisTypeface, 10, textBrush,
                new Point(0, y - 5), left - 4, TextAlignment.Right);
        }

        if (seriesList is null || seriesList.Count == 0 || seriesList.All(s => s.Points.Count < 2))
        {
            DrawText(context, "waiting for data…", AxisTypeface, 11, textBrush,
                new Point(left, top + plotH / 2 - 6), plotW, TextAlignment.Center);
            return;
        }

        foreach (var series in seriesList)
        {
            var pts = series.Points;
            if (pts.Count < 2) continue;

            Geometry? line;
            Geometry? area = null;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                Point Pt(int i)
                {
                    var x = left + plotW * i / (pts.Count - 1);
                    var raw = pts[i];
                    var ratio = double.IsFinite(raw) ? raw / maxValue : 0;
                    var y = top + plotH * (1 - Math.Clamp(ratio, 0, 1));
                    return new Point(x, y);
                }
                var first = Pt(0);
                g.BeginFigure(first, isFilled: false);
                for (var i = 1; i < pts.Count; i++)
                    g.LineTo(Pt(i));
                g.EndFigure(false);
                line = geo;

                if (series.Fill)
                {
                    var areaGeo = new StreamGeometry();
                    using (var g2 = areaGeo.Open())
                    {
                        g2.BeginFigure(new Point(first.X, top + plotH), isFilled: true);
                        for (var i = 0; i < pts.Count; i++)
                            g2.LineTo(Pt(i));
                        g2.LineTo(new Point(left + plotW, top + plotH));
                        g2.EndFigure(true);
                    }
                    area = areaGeo;
                }
            }

            if (area is not null)
                context.DrawGeometry(BrushWithAlpha(series.Stroke, 0.18), null, area);

            var pen = new Pen(series.Stroke, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, line);

            // Dot on the newest sample.
            var lastX = left + plotW;
            var lastRaw = pts[^1];
            var lastRatio = double.IsFinite(lastRaw) ? lastRaw / maxValue : 0;
            var lastY = top + plotH * (1 - Math.Clamp(lastRatio, 0, 1));
            context.DrawEllipse(series.Stroke, null, new Point(lastX, lastY), 2.6, 2.6);
        }

        // Legend: label + latest value per series.
        var legendY = top + plotH + 4;
        var x = left;
        foreach (var series in seriesList)
        {
            if (series.Label is null || series.Points.Count == 0) continue;
            var text = $"{series.Label} {FormatValue(series.Points[^1])}{Unit}";
            var size = Measure(text, LegendTypeface, 10);
            if (x + size.Width + 22 > bounds.Width) break;
            context.DrawLine(new Pen(series.Stroke, 2), new Point(x, legendY + 6), new Point(x + 12, legendY + 6));
            DrawText(context, text, LegendTypeface, 10, textBrush, new Point(x + 16, legendY), double.MaxValue, TextAlignment.Left);
            x += size.Width + 34;
        }
    }

    private static double ComputeMax(IReadOnlyList<ChartSeries>? seriesList)
    {
        var max = 1.0;
        if (seriesList != null)
            foreach (var s in seriesList)
                foreach (var p in s.Points)
                    if (double.IsFinite(p) && p > max) max = p;
        return max * 1.15;
    }

    private string FormatValue(double v)
    {
        if (v >= 1000) return (v / 1000).ToString("0.#k");
        if (v >= 100) return v.ToString("0");
        return v.ToString("0.#");
    }

    private static IBrush BrushWithAlpha(IBrush brush, double alpha)
    {
        if (brush is ISolidColorBrush solid)
            return new SolidColorBrush(solid.Color, alpha);
        return brush;
    }

    private static FormattedText Measure(string text, Typeface typeface, double size)
    {
        return new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, size, AxisBrush());
    }

    private static void DrawText(DrawingContext context, string text, Typeface typeface, double size,
        IBrush brush, Point origin, double maxWidth, TextAlignment alignment)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, size, brush)
        {
            TextAlignment = alignment,
            MaxTextWidth = maxWidth
        };
        context.DrawText(ft, origin);
    }
}
