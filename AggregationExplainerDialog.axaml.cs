using Avalonia.Controls;
using SchemaCompare.Services;

namespace SchemaCompare;

/// <summary>
/// The ✨ "legendary" keyword explainer: shows a hand-written explanation,
/// example T-SQL and tips for one SQL keyword. Prev/Next walks the library so
/// the dialog doubles as a tiny lesson browser.
/// </summary>
public partial class AggregationExplainerDialog : Window
{
    private int _index;

    private AggregationExplainerDialog(string keyword)
    {
        InitializeComponent();
        _index = SqlKeywordLibrary.Find(keyword) is { } found
            ? SqlKeywordLibrary.All.IndexOf(found)
            : 0;
        if (_index < 0) _index = 0;
        PrevBtn.Click += (_, _) => Show(_index - 1);
        NextBtn.Click += (_, _) => Show(_index + 1);
        CloseBtn.Click += (_, _) => Close();
        Show(_index);
    }

    public static Task ShowAsync(Window owner, string keyword)
    {
        var dlg = new AggregationExplainerDialog(keyword);
        return dlg.ShowDialog(owner);
    }

    private void Show(int index)
    {
        if ((uint)index >= SqlKeywordLibrary.All.Count) return;
        _index = index;
        var info = SqlKeywordLibrary.All[index];
        Title = $"✨ {info.Keyword} — SQL Keyword Explainer";
        KeywordText.Text = info.Keyword;
        CategoryText.Text = info.Category.ToUpperInvariant();
        TitleText.Text = info.Title;
        ExplanationText.Text = info.Explanation;
        ExampleBox.Text = info.Example;
        TipsText.Text = info.Tips;
        PrevBtn.IsEnabled = index > 0;
        NextBtn.IsEnabled = index < SqlKeywordLibrary.All.Count - 1;
    }
}
