using System.Collections.ObjectModel;
using System.Text.Json;

namespace SchemaCompare.Services;

/// <summary>
/// Most recently opened or saved .sql files, persisted as plain paths (no file
/// contents) in the data folder. Failures are logged, never thrown.
/// </summary>
public sealed class RecentFilesService
{
    public const int MaxEntries = 10;

    public ObservableCollection<string> Paths { get; } = [];

    private static string StorePath => Path.Combine(AppLog.LogDirectory, "recent_files.json");

    public void Load()
    {
        Paths.Clear();
        try
        {
            if (!File.Exists(StorePath)) return;
            var stored = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StorePath));
            if (stored == null) return;
            foreach (var path in stored.Where(p => !string.IsNullOrWhiteSpace(p)).Take(MaxEntries))
                Paths.Add(path);
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(RecentFilesService), ex, "Recent file list could not be read");
        }
    }

    /// <summary>Move <paramref name="path"/> to the top and persist the list.</summary>
    public void Touch(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var existing = Paths.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) Paths.Remove(existing);
        Paths.Insert(0, path);
        while (Paths.Count > MaxEntries) Paths.RemoveAt(Paths.Count - 1);
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
            File.WriteAllText(StorePath,
                JsonSerializer.Serialize(Paths.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(RecentFilesService), ex, "Recent file list could not be saved");
        }
    }
}
