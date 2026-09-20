using System.Text;
using System.Text.Json;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Loads/saves display settings to %AppData%/SchemaCompare/settings.json
/// and applies them to Application.Resources so every window updates live
/// via {DynamicResource UiScale} / {DynamicResource CodeFontSize}.
/// </summary>
public sealed class AppSettingsService
{
    private static readonly string StoreDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SchemaCompare");

    private static readonly string StorePath = Path.Combine(StoreDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath => StorePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return new AppSettings();
            var json = File.ReadAllText(StorePath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(StoreDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(StorePath, json, Encoding.UTF8);
    }

    /// <summary>Push values into Application.Resources (no-op when no app loaded, e.g. unit tests).</summary>
    public static void ApplyToResources(double uiScale, double codeFontSize)
    {
        try
        {
            var app = Avalonia.Application.Current;
            if (app is null)
                return;
            uiScale = Math.Clamp(uiScale, AppSettings.MinUiScale, AppSettings.MaxUiScale);
            codeFontSize = Math.Clamp(codeFontSize, AppSettings.MinCodeFontSize, AppSettings.MaxCodeFontSize);
            app.Resources["UiScale"] = uiScale;
            app.Resources["CodeFontSize"] = codeFontSize;
        }
        catch
        {
            // Resource update must never crash the app.
        }
    }

    public static void ApplyToResources(AppSettings settings)
    {
        settings.Normalize();
        ApplyToResources(settings.UiScale, settings.CodeFontSize);
    }
}
