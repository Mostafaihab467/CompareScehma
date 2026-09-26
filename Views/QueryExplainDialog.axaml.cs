using Avalonia.Controls;
using Avalonia.Media;
using SchemaCompare.Services;

namespace SchemaCompare.Views;

/// <summary>
/// Chat-style answer to "what will this query do?": a headline sentence plus a
/// step-by-step breakdown and deterministic safety warnings. Content is fully
/// parsed from the SQL text (SqlExplainerService) — no AI, no network.
/// </summary>
public partial class QueryExplainDialog : Window
{
    private QueryExplainDialog(string sql)
    {
        InitializeComponent();
        var explanation = SqlExplainerService.ExplainDetailed(sql);
        HeadlineText.Text = explanation.Headline;

        foreach (var bullet in explanation.Bullets)
        {
            BulletsList.Children.Add(new TextBlock
            {
                Text = bullet,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
                Foreground = Brush("PrimaryText", "#E7E9EE")
            });
        }
        if (BulletsList.Children.Count == 0)
            BulletsList.Children.Add(new TextBlock
            {
                Text = "No additional detail — the headline covers everything this script does.",
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap
            });

        if (explanation.Warnings.Count > 0)
        {
            WarningsPanel.IsVisible = true;
            foreach (var warning in explanation.Warnings)
            {
                WarningsList.Children.Add(new TextBlock
                {
                    Text = warning,
                    FontSize = 12.5,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("WarningPanelText", "#F3DC9C")
                });
            }
        }

        CloseBtn.Click += (_, _) => Close();
    }

    public static Task ShowAsync(Window owner, string sql)
    {
        var dlg = new QueryExplainDialog(sql);
        return dlg.ShowDialog(owner);
    }

    private static IBrush Brush(string key, string fallback) =>
        Avalonia.Application.Current?.TryGetResource(key, null, out var v) == true && v is IBrush b
            ? b
            : new SolidColorBrush(Color.Parse(fallback));
}
