using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using SchemaCompare.Services;

using SchemaCompare.Views;

namespace SchemaCompare.Controls;

/// <summary>
/// T-SQL editor with VS-style coloring, optional red squiggles for lint issues,
/// and optional green/red line backgrounds for schema-compare diffs.
/// </summary>
public sealed class SqlHighlightedEditor : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SqlHighlightedEditor, string?>(nameof(Text));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<SqlHighlightedEditor, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<bool> EnableLintProperty =
        AvaloniaProperty.Register<SqlHighlightedEditor, bool>(nameof(EnableLint));

    public static readonly StyledProperty<string?> PartnerTextProperty =
        AvaloniaProperty.Register<SqlHighlightedEditor, string?>(nameof(PartnerText));

    /// <summary>"Source" paints unmatched lines green; "Target" paints them red.</summary>
    public static readonly StyledProperty<string?> DiffRoleProperty =
        AvaloniaProperty.Register<SqlHighlightedEditor, string?>(nameof(DiffRole));

    public static readonly StyledProperty<string> LintSummaryProperty =
        AvaloniaProperty.Register<SqlHighlightedEditor, string>(nameof(LintSummary), "");

    /// <summary>IntelliSense is ON by default so every window that hosts this
    /// control inherits completion without extra wiring.</summary>
    public static readonly StyledProperty<bool> EnableIntelliSenseProperty =
        AvaloniaProperty.Register<SqlHighlightedEditor, bool>(nameof(EnableIntelliSense), true);

    private readonly TextEditor _editor = new();
    private readonly SqlSquiggleRenderer _squiggles = new();
    private readonly SqlLineHighlightRenderer _lines = new();
    private readonly EditorFindBar _findBar;
    private readonly EditorFolding _folding;
    private readonly EditorBookmarks _bookmarks;
    private readonly EditorGotoLine _gotoLine;
    private readonly DispatcherTimer _lintTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly Popup _lintTip;
    private readonly TextBlock _lintTipText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, MaxWidth = 420 };
    private bool _syncing;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool EnableLint
    {
        get => GetValue(EnableLintProperty);
        set => SetValue(EnableLintProperty, value);
    }

    public string? PartnerText
    {
        get => GetValue(PartnerTextProperty);
        set => SetValue(PartnerTextProperty, value);
    }

    public string? DiffRole
    {
        get => GetValue(DiffRoleProperty);
        set => SetValue(DiffRoleProperty, value);
    }

    public string LintSummary
    {
        get => GetValue(LintSummaryProperty);
        set => SetValue(LintSummaryProperty, value);
    }

    public bool EnableIntelliSense
    {
        get => GetValue(EnableIntelliSenseProperty);
        set => SetValue(EnableIntelliSenseProperty, value);
    }

    public TextEditor InnerEditor => _editor;

    /// <summary>Find &amp; replace strip shared by every SQL surface.</summary>
    public EditorFindBar FindBar => _findBar;

    /// <summary>Collapse/expand comments, BEGIN…END blocks and GO batches (Ctrl+M).</summary>
    public EditorFolding Folding => _folding;

    /// <summary>Per-line bookmarks with next/prev navigation (Ctrl+B / Ctrl+K).</summary>
    public EditorBookmarks Bookmarks => _bookmarks;

    /// <summary>Ctrl+G go-to-line overlay.</summary>
    public EditorGotoLine GotoLine => _gotoLine;

    public SqlHighlightedEditor()
    {
        TsqlHighlighting.Apply(_editor);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_lines);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_squiggles);
        _findBar = new EditorFindBar(_editor);
        _folding = new EditorFolding(_editor);
        _bookmarks = new EditorBookmarks(_editor);
        _gotoLine = new EditorGotoLine(_editor);

        // Dedicated hover popup for lint squiggles. A ToolTip forced open via
        // SetIsOpen on every PointerMoved sticks/flickers because it re-shows
        // while the pointer travels; this popup is closed explicitly on leave.
        _lintTip = new Popup
        {
            Placement = PlacementMode.Pointer,
            PlacementTarget = _editor.TextArea,
            Child = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5),
                BorderThickness = new Thickness(1),
                Background = ResolveBrush("ErrorPanelBg", Color.Parse("#2C1215")),
                BorderBrush = ResolveBrush("ErrorPanelBorder", Color.Parse("#F87171")),
                Child = _lintTipText
            }
        };
        _lintTipText.Foreground = ResolveBrush("ErrorPanelText", Color.Parse("#FCA5A5"));

        // Popup must live in the tree to resolve its TopLevel for positioning.
        Content = new Panel { Children = { _editor, _lintTip, _findBar.Host, _gotoLine.Host } };

        _editor.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            _syncing = true;
            try { Text = _editor.Text; }
            finally { _syncing = false; }
            HideLintTip();
            ScheduleLint();
            RefreshDiff();
            if (_findBar.IsOpen) _findBar.Rebuild();
        };

        _lintTimer.Tick += (_, _) =>
        {
            _lintTimer.Stop();
            RunLint();
        };

        _editor.TextArea.PointerMoved += OnPointerMoved;
        _editor.TextArea.PointerExited += (_, _) => HideLintTip();
        _editor.TextArea.KeyDown += OnEditorKeyDown;
        ApplyIntelliSense();
        SqlCompletionProvider.SchemaChanged += (_, _) =>
            Dispatcher.UIThread.Post(ScheduleLint);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        HandleEditorChord(e);
        if (!EnableIntelliSense) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Space)
        {
            SqlCompletionProvider.Close();
            SqlCompletionProvider.Show(_editor.TextArea, _editor.CaretOffset);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Routes Ctrl+F/H, F3, Ctrl+M, Ctrl+B/K and Ctrl+G to the shared editor
    /// helpers. Also called from QueryWindow when focus sits on the result grid,
    /// so the chords work without clicking into the script first.
    /// </summary>
    public bool HandleEditorChord(KeyEventArgs e)
    {
        // The completion popup owns Escape/F3 while it is open.
        if (SqlCompletionProvider.IsPopupOpen) return false;
        if (_findBar.HandleKeyDown(e) || _folding.HandleKeyDown(e) ||
            _bookmarks.HandleKeyDown(e) || _gotoLine.HandleKeyDown(e))
        {
            e.Handled = true;
            return true;
        }
        return false;
    }

    private void ApplyIntelliSense()
    {
        if (EnableIntelliSense)
            SqlCompletionProvider.Attach(_editor);
        else
            SqlCompletionProvider.Detach(_editor);
    }

    private static IBrush ResolveBrush(string key, Color fallback)
    {
        if (Application.Current?.TryGetResource(key, null, out var value) == true)
        {
            if (value is IBrush brush) return brush;
            if (value is Color color) return new SolidColorBrush(color);
        }
        return new SolidColorBrush(fallback);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            var next = Text ?? "";
            if (!_syncing && _editor.Text != next)
            {
                _syncing = true;
                try { _editor.Text = next; }
                finally { _syncing = false; }
            }
            ScheduleLint();
            RefreshDiff();
            if (_findBar.IsOpen) _findBar.Rebuild();
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            _editor.IsReadOnly = IsReadOnly;
        }
        else if (change.Property == EnableLintProperty)
        {
            ScheduleLint();
        }
        else if (change.Property == EnableIntelliSenseProperty)
        {
            ApplyIntelliSense();
        }
        else if (change.Property == PartnerTextProperty || change.Property == DiffRoleProperty)
        {
            RefreshDiff();
        }
    }

    public void AttachCompletion() => SqlCompletionProvider.Attach(_editor);

    private void ScheduleLint()
    {
        if (!EnableLint)
        {
            _squiggles.Issues = [];
            LintSummary = "";
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            return;
        }
        _lintTimer.Stop();
        _lintTimer.Start();
    }

    private void RunLint()
    {
        try
        {
            var issues = SqlLintService.Analyze(
                _editor.Text ?? "",
                SqlCompletionProvider.Tables,
                SqlCompletionProvider.ColumnsByTable);
            _squiggles.Issues = issues;
            if (issues.Count == 0)
                HideLintTip();
            LintSummary = issues.Count == 0
                ? ""
                : issues.Count == 1
                    ? "1 issue — " + issues[0].Message
                    : $"{issues.Count} issues — " + issues[0].Message;
            if (DataContext is Models.QueryTab tab)
                tab.LintSummary = LintSummary;
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        }
        catch
        {
            /* lint must never break editing */
        }
    }

    private void RefreshDiff()
    {
        var role = DiffRole;
        if (string.IsNullOrEmpty(role))
        {
            _lines.LineNumbers = [];
            _lines.Fill = null;
            _lines.Border = null;
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            return;
        }
        _lines.LineNumbers = LineDiffer.UniqueLines(_editor.Text, PartnerText);
        var isSource = string.Equals(role, "Source", StringComparison.OrdinalIgnoreCase);
        _lines.Fill = isSource
            ? new SolidColorBrush(Color.Parse("#382EA043"))  // green: added in source
            : new SolidColorBrush(Color.Parse("#38DA3633")); // red: only in target
        _lines.Border = isSource
            ? new SolidColorBrush(Color.Parse("#34D399"))   // bright green left accent
            : new SolidColorBrush(Color.Parse("#F87171"));  // bright red left accent
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        try
        {
            if (SqlCompletionProvider.IsPopupOpen || _squiggles.Issues.Count == 0 || _editor.Document == null)
            {
                HideLintTip();
                return;
            }
            var textView = _editor.TextArea.TextView;
            var pos = e.GetPosition(textView);
            var docPos = textView.GetPosition(pos + textView.ScrollOffset);
            var offset = docPos == null ? -1 : _editor.Document.GetOffset(docPos.Value.Location);
            var hit = offset < 0
                ? (SqlLintIssue?)null
                : _squiggles.Issues.FirstOrDefault(i => offset >= i.Start && offset <= i.Start + Math.Max(1, i.Length));
            if (string.IsNullOrEmpty(hit?.Message))
            {
                HideLintTip();
                return;
            }
            ShowLintTip(hit.Value.Message);
        }
        catch
        {
            /* ignore hover failures */
        }
    }

    private void ShowLintTip(string message)
    {
        if (_lintTipText.Text != message)
        {
            // Reopen with fresh placement; updating while open keeps stale coordinates.
            _lintTip.IsOpen = false;
            _lintTipText.Text = message;
        }
        _lintTip.IsOpen = true;
    }

    private void HideLintTip()
    {
        if (_lintTip.IsOpen)
            _lintTip.IsOpen = false;
    }
}

internal sealed class SqlSquiggleRenderer : IBackgroundRenderer
{
    public IReadOnlyList<SqlLintIssue> Issues { get; set; } = [];
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Issues.Count == 0 || textView.VisualLines.Count == 0 || textView.Document == null)
            return;
        var pen = new Pen(new SolidColorBrush(Color.Parse("#EF4444")), 1.3);
        foreach (var issue in Issues)
        {
            var start = Math.Clamp(issue.Start, 0, textView.Document.TextLength);
            var end = Math.Clamp(issue.Start + Math.Max(1, issue.Length), start, textView.Document.TextLength);
            foreach (var rect in MarkerRects(textView, start, end))
                DrawSquiggle(drawingContext, pen, rect);
        }
    }

    internal static IEnumerable<Rect> MarkerRects(TextView textView, int start, int end)
    {
        var doc = textView.Document;
        if (doc == null || start >= end) yield break;
        var len = Math.Min(end - start, doc.TextLength - start);
        if (len <= 0) yield break;

        var segment = new TextSegment { StartOffset = start, Length = len };
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            var y = rect.Bottom - 2;
            yield return new Rect(rect.X, y - 2, Math.Max(6, rect.Width), 3);
        }
    }

    private static void DrawSquiggle(DrawingContext dc, Pen pen, Rect rect)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            var y = rect.Y + 1;
            var x = rect.X;
            var up = true;
            g.BeginFigure(new Point(x, y), false);
            while (x < rect.Right)
            {
                x += 3;
                y = up ? rect.Y : rect.Bottom;
                up = !up;
                g.LineTo(new Point(Math.Min(x, rect.Right), y));
            }
            g.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, geo);
    }
}

internal sealed class SqlLineHighlightRenderer : IBackgroundRenderer
{
    public HashSet<int> LineNumbers { get; set; } = [];
    public IBrush? Fill { get; set; }
    public IBrush? Border { get; set; }
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Fill == null || LineNumbers.Count == 0 || textView.VisualLines.Count == 0)
            return;
        var width = Math.Max(textView.Bounds.Width, 3000);
        foreach (var vl in textView.VisualLines)
        {
            var num = vl.FirstDocumentLine.LineNumber;
            if (!LineNumbers.Contains(num)) continue;
            var y = vl.VisualTop - textView.VerticalOffset;
            drawingContext.FillRectangle(Fill, new Rect(0, y, width, vl.Height));
            if (Border != null)
                drawingContext.FillRectangle(Border, new Rect(0, y, 4, vl.Height));
        }
    }
}
