using Avalonia.Controls;
using SchemaCompare.Controls;

namespace SchemaCompare.Views;

public partial class ScriptActionDialog : Window
{
    private bool _finished;

    private ScriptActionDialog(string title, string warning, string script)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;

        if (!string.IsNullOrWhiteSpace(warning))
        {
            WarningText.Text = warning;
            WarningPanel.IsVisible = true;
        }
        ScriptBox.Text = script;

        ExecuteBtn.Click += (_, _) => Finish(true);
        CancelBtn.Click += (_, _) => Finish(false);
    }

    /// <summary>True = run the script, false/null = cancel.</summary>
    public static Task<bool> ShowAsync(Window owner, string title, string warning, string script)
    {
        var dlg = new ScriptActionDialog(title, warning, script);
        return dlg.ShowDialog<bool>(owner);
    }

    private void Finish(bool execute)
    {
        if (_finished) return;
        _finished = true;
        Close(execute);
    }
}
