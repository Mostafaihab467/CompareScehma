using Avalonia;

namespace SchemaCompare.Services;

/// <summary>
/// Applies the user's text-size scale to the app-wide font resources (F10…F64).
/// All windows reference these via {DynamicResource Fxx}, so one call rescales
/// every open window live. Base values match Resources.axaml.
/// </summary>
public static class UiFontSizes
{
    public static readonly IReadOnlyDictionary<string, double> Base = new Dictionary<string, double>
    {
        ["F10"] = 10, ["F10p5"] = 10.5, ["F11"] = 11, ["F11p5"] = 11.5,
        ["F12"] = 12, ["F12p5"] = 12.5, ["F13"] = 13, ["F14"] = 14,
        ["F15"] = 15, ["F16"] = 16, ["F18"] = 18, ["F20"] = 20,
        ["F21"] = 21, ["F22"] = 22, ["F24"] = 24, ["F32"] = 32,
        ["F56"] = 56, ["F64"] = 64,
    };

    public static void Apply(double scale)
    {
        var app = Application.Current;
        if (app is null) return;
        var clamped = Math.Clamp(scale, UiSettingsService.MinFontScale, UiSettingsService.MaxFontScale);
        foreach (var (key, baseSize) in Base)
        {
            try { app.Resources[key] = Math.Round(baseSize * clamped, 1); }
            catch { /* one bad resource must not break the rest */ }
        }
    }
}
