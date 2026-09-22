namespace SchemaCompare.Models;

/// <summary>
/// Persisted display preferences: UI zoom (scales text via layout transform)
/// and code/details font size. Stored in %AppData%/SchemaCompare/settings.json.
/// </summary>
public sealed class AppSettings
{
    public const double DefaultUiScale = 0.95;
    public const double MinUiScale = 0.7;
    public const double MaxUiScale = 1.4;

    public const double DefaultCodeFontSize = 12.0;
    public const double MinCodeFontSize = 10.0;
    public const double MaxCodeFontSize = 20.0;

    public double UiScale { get; set; } = DefaultUiScale;
    public double CodeFontSize { get; set; } = DefaultCodeFontSize;

    public AppSettings Clone() => new() { UiScale = UiScale, CodeFontSize = CodeFontSize };

    public void Normalize()
    {
        if (double.IsNaN(UiScale) || double.IsInfinity(UiScale))
            UiScale = DefaultUiScale;
        if (double.IsNaN(CodeFontSize) || double.IsInfinity(CodeFontSize))
            CodeFontSize = DefaultCodeFontSize;
        UiScale = Math.Clamp(UiScale, MinUiScale, MaxUiScale);
        CodeFontSize = Math.Clamp(CodeFontSize, MinCodeFontSize, MaxCodeFontSize);
    }
}
