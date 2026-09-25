using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SchemaCompare.Controls;

/// <summary>
/// ComboBox with a small search bar injected above the dropdown list. Drop-in
/// replacement for ComboBox — selection, templates and bindings are untouched;
/// filtering only collapses non-matching item containers. The bar hides itself
/// for short lists (see <see cref="ShowSearchThreshold"/>) so small option
/// dropdowns (AND/OR, ASC/DESC…) stay clean, and it appears automatically for
/// long ones (connections, tables, columns, databases…).
/// </summary>
public class SearchableComboBox : ComboBox
{
    public static readonly StyledProperty<int> ShowSearchThresholdProperty =
        AvaloniaProperty.Register<SearchableComboBox, int>(nameof(ShowSearchThreshold), 8);

    /// <summary>Dropdowns with more items than this show the search bar.</summary>
    public int ShowSearchThreshold
    {
        get => GetValue(ShowSearchThresholdProperty);
        set => SetValue(ShowSearchThresholdProperty, value);
    }

    private TextBox? _search;
    private Popup? _popup;
    private Control? _wrapper;
    private Control? _templateContent;
    private string _filter = "";

    // Theme lookup is keyed by the concrete type name; without this the Fluent
    // ComboBox template never applies and the dropdown has no popup at all.
    protected override Type StyleKeyOverride => typeof(ComboBox);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_popup != null)
        {
            _popup.Opened -= OnPopupOpened;
            _popup.Closed -= OnPopupClosed;
        }
        _popup = e.NameScope.Find<Popup>("PART_Popup")
                 ?? this.GetVisualDescendants().OfType<Popup>().FirstOrDefault();
        if (_popup == null)
            return; // unknown template — degrade to a plain ComboBox
        _popup.Opened -= OnPopupOpened;
        _popup.Opened += OnPopupOpened;
        _popup.Closed -= OnPopupClosed;
        _popup.Closed += OnPopupClosed;
        LayoutUpdated -= OnLayoutUpdated;
        LayoutUpdated += OnLayoutUpdated;
        // Inject outside the measure pass: swapping the popup child mid-measure
        // breaks logical re-parenting; a posted call runs on a settled tree.
        Dispatcher.UIThread.Post(InjectSafe);
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        if (_search == null || !ReferenceEquals(_popup?.Child, _wrapper))
            InjectSafe();
        if (_search == null)
            return;
        _filter = "";
        _search.Text = "";
        _search.IsVisible = ItemCount > ShowSearchThreshold;
        if (_search.IsVisible)
        {
            ApplyFilterToContainers();
            Dispatcher.UIThread.Post(() => _search.Focus());
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        _filter = "";
        if (_search != null)
            _search.Text = "";
        ApplyFilterToContainers();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_popup is { IsOpen: true } && (_search == null || !ReferenceEquals(_popup.Child, _wrapper)))
            InjectSafe();
        // Containers realize lazily while scrolling — re-apply so newly
        // realized (or recycled) containers respect the active filter.
        if (_filter.Length > 0)
            ApplyFilterToContainers();
    }

    /// <summary>Diagnostics: last injection failure (null when injection works).</summary>
    internal static Exception? LastInjectError;

    private void InjectSafe()
    {
        if (_popup == null)
            return;
        try
        {
            InjectSearchBar();
        }
        catch (Exception ex)
        {
            LastInjectError = ex;
            // Restore the template content; degrade to a plain dropdown.
            try
            {
                if (_templateContent is ISetLogicalParent restore)
                    restore.SetParent(null);
                if (_templateContent != null)
                    _popup.Child = _templateContent;
            }
            catch { /* leave as-is */ }
            _search = null;
            _wrapper = null;
        }
    }

    private void InjectSearchBar()
    {
        if (_popup == null)
            return;
        var original = _popup.Child;
        if (original == null)
            return;
        _templateContent = original;

        _search = new TextBox
        {
            PlaceholderText = "🔍  Search…",
            Margin = new Thickness(6, 6, 6, 4),
            MinHeight = 28,
            FontSize = 12,
            CornerRadius = new CornerRadius(4),
            Background = ResolveBrush("CodeBg", Color.Parse("#0E0E15")),
            Foreground = ResolveBrush("PrimaryText", Color.Parse("#F2F2F7")),
            IsVisible = ItemCount > ShowSearchThreshold
        };
        _search.TextChanged += (_, _) => ApplyFilter(_search.Text ?? "");
        _search.KeyDown += OnSearchKeyDown;

        var panel = new StackPanel
        {
            Background = ResolveBrush("SubtleHeader", Color.Parse("#1D1D2A")),
            Children = { _search }
        };
        // Order matters: detach the template content from the popup's logical
        // scope, assign the panel to the popup, then adopt the content — doing
        // it in any other order leaves the border mid-attach without a parent.
        _wrapper = panel;
        if (original is ISetLogicalParent settable)
            settable.SetParent(null);
        _popup.Child = panel;
        panel.Children.Add(original);
        // The list lives in a PopupRoot — a separate layout root from the
        // combo — so only the wrapper's LayoutUpdated fires for popup passes.
        // Realization happens lazily there; re-apply the filter on each pass.
        panel.LayoutUpdated -= OnPopupContentLayoutUpdated;
        panel.LayoutUpdated += OnPopupContentLayoutUpdated;
    }

    private void OnPopupContentLayoutUpdated(object? sender, EventArgs e)
    {
        if (_filter.Length > 0)
            ApplyFilterToContainers();
    }

    private void ApplyFilter(string text)
    {
        _filter = text.Trim();
        ApplyFilterToContainers();
    }

    private void ApplyFilterToContainers()
    {
        var panel = ItemsPanelRoot;
        if (panel == null)
            return;
        foreach (var child in panel.Children.OfType<ComboBoxItem>())
            child.IsVisible = _filter.Length == 0 || MatchText(child.Content).Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The string an item is matched against: dedicated display
    /// properties first (DisplayName/Name/Title/Label/Text), then ToString().</summary>
    private static string MatchText(object? item)
    {
        if (item == null) return "";
        if (item is string s) return s;
        var type = item.GetType();
        foreach (var prop in new[] { "DisplayName", "Name", "Title", "Label", "Text" })
            if (type.GetProperty(prop)?.GetValue(item) is string v && v.Length > 0)
                return v;
        return item.ToString() ?? "";
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                AcceptFirstVisible();
                e.Handled = true;
                break;
            case Key.Escape:
                IsDropDownOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        var visible = ItemsPanelRoot?.Children.OfType<ComboBoxItem>().Where(c => c.IsVisible).ToList();
        if (visible is not { Count: > 0 })
            return;
        var current = visible.FindIndex(c => Equals(c.Content, SelectedItem));
        var next = current < 0
            ? (delta > 0 ? 0 : visible.Count - 1)
            : Math.Clamp(current + delta, 0, visible.Count - 1);
        SelectedItem = visible[next].Content;
    }

    private void AcceptFirstVisible()
    {
        if (_search is { Text.Length: > 0 } &&
            ItemsPanelRoot?.Children.OfType<ComboBoxItem>().FirstOrDefault(c => c.IsVisible) is { } first)
            SelectedItem = first.Content;
        IsDropDownOpen = false;
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
}
