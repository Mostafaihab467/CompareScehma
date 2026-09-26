using System.Text.Json;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>One tab as stored between runs.</summary>
public sealed class TabSessionEntry
{
    public string Title { get; set; } = "";

    /// <summary>Set when the tab is backed by a .sql file; the text is then read from disk.</summary>
    public string? FilePath { get; set; }

    /// <summary>Unsaved tabs keep their text so a restart does not destroy work in progress.</summary>
    public string? SqlText { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>
/// Persists which query tabs were open, so closing the window does not silently
/// discard them. File-backed tabs store only the path; scratch tabs store their
/// text, truncated and capped, because that is the only copy that exists.
/// Read and write failures are logged, never thrown.
/// </summary>
public sealed class TabSessionService
{
    public const int MaxTabs = 24;
    public const int MaxScratchChars = 100_000;

    private static string StorePath => Path.Combine(AppLog.LogDirectory, "tab_session.json");

    public List<TabSessionEntry> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            var stored = JsonSerializer.Deserialize<List<TabSessionEntry>>(File.ReadAllText(StorePath));
            return stored?.Where(e => e != null &&
                                      (!string.IsNullOrWhiteSpace(e.FilePath) || e.SqlText != null)).ToList()
                   ?? [];
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(TabSessionService), ex, "Saved tab session could not be read");
            return [];
        }
    }

    /// <summary>Stores the current tab set, keeping the active tab marked.</summary>
    public void Save(IReadOnlyList<QueryTab> tabs, QueryTab? active)
    {
        try
        {
            var entries = tabs.Take(MaxTabs).Select(t => new TabSessionEntry
            {
                Title = t.Title,
                FilePath = string.IsNullOrWhiteSpace(t.FilePath) ? null : t.FilePath,
                SqlText = string.IsNullOrWhiteSpace(t.FilePath) ? Truncate(t.SqlText) : null,
                IsActive = ReferenceEquals(t, active)
            }).ToList();

            Directory.CreateDirectory(AppLog.LogDirectory);
            File.WriteAllText(StorePath,
                JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(TabSessionService), ex, "Tab session could not be saved");
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(StorePath)) File.Delete(StorePath);
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(TabSessionService), ex, "Saved tab session could not be cleared");
        }
    }

    private static string? Truncate(string? sql) =>
        sql == null ? null : sql.Length > MaxScratchChars ? sql[..MaxScratchChars] : sql;
}
