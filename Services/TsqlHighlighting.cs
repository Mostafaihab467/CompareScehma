using System.Xml;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace SchemaCompare.Services;

/// <summary>Loads the bundled T-SQL highlighting definition (VS-style colors).</summary>
public static class TsqlHighlighting
{
    private static IHighlightingDefinition? _cached;
    private static readonly object Gate = new();

    public static IHighlightingDefinition? Get()
    {
        if (_cached != null) return _cached;
        lock (Gate)
        {
            if (_cached != null) return _cached;
            try
            {
                var asm = typeof(TsqlHighlighting).Assembly;
                using var stream = asm.GetManifestResourceStream("SchemaCompare.Resources.TSQL.xshd");
                if (stream != null)
                {
                    using var reader = XmlReader.Create(stream);
                    var xshd = HighlightingLoader.LoadXshd(reader);
                    _cached = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
                    return _cached;
                }
            }
            catch
            {
                /* fall through */
            }
            _cached = HighlightingManager.Instance.GetDefinition("SQL");
            return _cached;
        }
    }

    public static void Apply(TextEditor editor)
    {
        editor.SyntaxHighlighting = Get();
        editor.ShowLineNumbers = true;
        editor.FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,Monospace");
        editor.Background = new SolidColorBrush(Color.Parse("#1E1E2E"));
        editor.Foreground = new SolidColorBrush(Color.Parse("#E6E6F2"));
        editor.LineNumbersForeground = new SolidColorBrush(Color.Parse("#6C7086"));
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;
        editor.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        editor.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        try
        {
            if (Avalonia.Application.Current?.TryGetResource("CodeFontSize", null, out var v) == true &&
                v is double size)
                editor.FontSize = size;
            else
                editor.FontSize = 13;
        }
        catch
        {
            editor.FontSize = 13;
        }
    }
}
