using System.Text;
using System.Text.Json;

namespace SchemaCompare.Services;

/// <summary>
/// Persists UI preferences (%AppData%/SchemaCompare/ui_settings.json).
/// Currently just the text-size scale; extend with new properties as needed.
/// </summary>
public sealed class UiSettingsService
{
    private static readonly string StoreDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SchemaCompare");

    private static readonly string StorePath = Path.Combine(StoreDir, "ui_settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public const double DefaultFontScale = 0.9;
    public const double MinFontScale = 0.75;
    public const double MaxFontScale = 1.3;

    public double LoadFontScale()
    {
        try
        {
            if (!File.Exists(StorePath))
                return DefaultFontScale;
            var json = File.ReadAllText(StorePath, Encoding.UTF8);
            var dto = JsonSerializer.Deserialize<UiSettingsDto>(json, JsonOptions);
            if (dto is null || double.IsNaN(dto.FontScale) || double.IsInfinity(dto.FontScale))
                return DefaultFontScale;
            return Math.Clamp(dto.FontScale, MinFontScale, MaxFontScale);
        }
        catch
        {
            return DefaultFontScale;
        }
    }

    public void SaveFontScale(double scale)
    {
        try
        {
            Directory.CreateDirectory(StoreDir);
            var dto = new UiSettingsDto { FontScale = Math.Clamp(scale, MinFontScale, MaxFontScale) };
            File.WriteAllText(StorePath, JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8);
        }
        catch { /* preferences must never crash the app */ }
    }

    private sealed class UiSettingsDto
    {
        public double FontScale { get; set; } = DefaultFontScale;
    }
}
