using System.Text;
using System.Text.Json;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Persists diagram layouts (node positions, visibility, zoom) as JSON.
/// Auto-saves per database under %AppData%/SchemaCompare/Diagrams so a
/// reloaded schema restores the user's arrangement; explicit Save As / Open
/// supports sharing layout files.
/// </summary>
public sealed class DiagramPersistenceService
{
    private static readonly string StoreDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SchemaCompare", "Diagrams");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string GetAutoSavePath(string server, string database)
    {
        var safe = string.Concat($"{server}_{database}".Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        if (safe.Length > 80) safe = safe[..80];
        if (string.IsNullOrWhiteSpace(safe.Trim('_'))) safe = "default";
        return Path.Combine(StoreDir, safe + ".diagram.json");
    }

    public void Save(DiagramPersistedState state)
    {
        Directory.CreateDirectory(StoreDir);
        SaveToFile(GetAutoSavePath(state.Server, state.Database), state);
    }

    public DiagramPersistedState? TryLoad(string server, string database)
    {
        var path = GetAutoSavePath(server, database);
        return File.Exists(path) ? LoadFromFile(path) : null;
    }

    public void SaveToFile(string path, DiagramPersistedState state)
    {
        state.SavedAtUtc = DateTime.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? StoreDir);
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions), Encoding.UTF8);
    }

    public DiagramPersistedState? LoadFromFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<DiagramPersistedState>(json, JsonOptions);
            return state is { Tables.Count: > 0 } ? state : null;
        }
        catch
        {
            return null;
        }
    }

    /// <returns>Number of tables the saved layout was applied to.</returns>
    public int ApplyToTables(
        DiagramPersistedState state,
        IEnumerable<DiagramTableNode> tables,
        out double zoom)
    {
        zoom = state.Zoom is >= 0.4 and <= 2.0 ? state.Zoom : 1.0;
        var saved = state.Tables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
        var applied = 0;
        foreach (var node in tables)
        {
            if (!saved.TryGetValue(node.FullName, out var s)) continue;
            node.X = Math.Max(0, s.X);
            node.Y = Math.Max(0, s.Y);
            node.IsVisible = s.IsVisible;
            applied++;
        }
        return applied;
    }
}
