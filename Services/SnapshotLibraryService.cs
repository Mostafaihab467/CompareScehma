using System.Collections.ObjectModel;
using System.Text.Json;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// The baselines this app has captured or compared against, so "compare against yesterday's
/// snapshot" is a pick from a list rather than a trip through a file dialog to a folder of
/// timestamped files.
///
/// Only the path and the provenance already stored inside the package are kept — no
/// connection details, and nothing that could be mistaken for a credential. Forgetting an
/// entry removes the record; the file is the operator's and is never touched.
/// </summary>
public sealed class SnapshotLibraryService
{
    public const int MaxEntries = 20;

    public ObservableCollection<SnapshotEntry> Entries { get; } = [];

    private static string StorePath => Path.Combine(AppLog.LogDirectory, "snapshot_library.json");

    public void Load()
    {
        Entries.Clear();
        try
        {
            if (!File.Exists(StorePath)) return;
            var stored = JsonSerializer.Deserialize<List<SnapshotEntry>>(File.ReadAllText(StorePath));
            if (stored == null) return;
            // A baseline whose file has been moved or deleted is not a choice, it is a dead end;
            // dropping it here also keeps the stored list from growing back on every save.
            foreach (var entry in stored.Where(e => !string.IsNullOrWhiteSpace(e.Path) && File.Exists(e.Path))
                         .Take(MaxEntries))
                Entries.Add(entry);
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(SnapshotLibraryService), ex, "Snapshot library could not be read");
        }
    }

    /// <summary>Record (or re-order) a snapshot after a capture or a browse, newest first.</summary>
    public SnapshotEntry Remember(SnapshotInfo info)
    {
        var entry = SnapshotEntry.From(info);
        return Remember(entry);
    }

    public SnapshotEntry Remember(SnapshotEntry entry)
    {
        var existing = Entries.FirstOrDefault(
            e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        // The card re-describes the same file on every visit, and the path box fires while the
        // operator types. Only re-order and rewrite the file when something really changed.
        if (ReferenceEquals(existing, entry)) return entry;
        if (existing != null && Entries[0] == existing && existing.SameProvenanceAs(entry)) return existing;
        if (existing != null) Entries.Remove(existing);
        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
        Save();
        return entry;
    }

    /// <summary>Find the remembered baseline a path points at, so a card filled from anywhere
    /// shows the same row in the list.</summary>
    public SnapshotEntry? Find(string? path) => string.IsNullOrWhiteSpace(path)
        ? null
        : Entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Drop the record only. The .dacpac stays exactly where it is.</summary>
    public bool Forget(SnapshotEntry entry)
    {
        var removed = Entries.Remove(entry);
        if (removed) Save();
        return removed;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Entries.ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(SnapshotLibraryService), ex, "Snapshot library could not be saved");
        }
    }
}
