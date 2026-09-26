using System.Collections.ObjectModel;
using System.Text.Json;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// The pairings the operator compares over and over — a live database against a captured
/// baseline, staging against production — so a weekly drift check is one pick instead of
/// rebuilding both cards from memory.
///
/// Each side is stored as a reference (a saved profile's id, or a snapshot path), which is why
/// nothing here can hold a credential: passwords live in the profile store, protected, and a
/// comparison only names the profile to read them from. A pairing whose profile has since been
/// deleted is kept rather than pruned — the operator named it, and applying it says exactly
/// which side is missing instead of silently dropping the workflow.
/// </summary>
public sealed class SavedComparisonsService
{
    public const int MaxEntries = 20;

    public ObservableCollection<SavedComparison> Entries { get; } = [];

    private static string StorePath => Path.Combine(AppLog.LogDirectory, "saved_comparisons.json");

    public void Load()
    {
        Entries.Clear();
        try
        {
            if (!File.Exists(StorePath)) return;
            var stored = JsonSerializer.Deserialize<List<SavedComparison>>(File.ReadAllText(StorePath));
            if (stored == null) return;
            foreach (var entry in stored.Where(e => !string.IsNullOrWhiteSpace(e.Name)).Take(MaxEntries))
                Entries.Add(entry);
            SortByName();
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(SavedComparisonsService), ex, "Saved comparisons could not be read");
        }
    }

    public SavedComparison? Find(string? name) => string.IsNullOrWhiteSpace(name)
        ? null
        : Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Add or replace by name, keeping the list alphabetical: these are named workflows
    /// to choose from, not a recent-files trail. A new name at a full list is refused rather than
    /// silently evicting a pairing the operator named on purpose, and the row being re-saved keeps
    /// its place instead of resetting the list a dropdown is bound to.</summary>
    public SavedComparison Save(SavedComparison comparison)
    {
        if (string.IsNullOrWhiteSpace(comparison.Name))
            throw new InvalidOperationException("Give the comparison a name before saving it.");
        var existing = Find(comparison.Name);
        if (existing == null && Entries.Count >= MaxEntries)
            throw new InvalidOperationException(
                $"{MaxEntries} comparisons is all this list keeps — forget one before saving another.");
        if (existing != null) Entries.Remove(existing);

        var index = 0;
        while (index < Entries.Count &&
               string.Compare(Entries[index].Name, comparison.Name, StringComparison.OrdinalIgnoreCase) < 0)
            index++;
        Entries.Insert(index, comparison);
        Save();
        return comparison;
    }

    public bool Forget(SavedComparison comparison)
    {
        var removed = Entries.Remove(comparison);
        if (removed) Save();
        return removed;
    }

    private void SortByName()
    {
        var ordered = Entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Entries.Clear();
        foreach (var entry in ordered) Entries.Add(entry);
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
            AppLog.Error(nameof(SavedComparisonsService), ex, "Saved comparisons could not be saved");
        }
    }
}
