using System.Text.Json;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Query history persisted as JSON in the data folder, newest first and capped so
/// the file stays small. Read and write failures are logged, never thrown.
/// </summary>
public sealed class QueryHistoryService
{
    public const int MaxEntries = 200;
    public const int MaxSqlChars = 20_000;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string StorePath => Path.Combine(AppLog.LogDirectory, "query_history.json");

    public List<QueryHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            var stored = JsonSerializer.Deserialize<List<QueryHistoryEntry>>(File.ReadAllText(StorePath));
            return stored ?? [];
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(QueryHistoryService), ex, "Query history could not be read");
            return [];
        }
    }

    /// <summary>Adds a run to the front of the store, trimming the oldest entries.</summary>
    public List<QueryHistoryEntry> Add(QueryHistoryEntry entry)
    {
        var all = Load();
        all.Insert(0, entry);
        if (entry.Sql?.Length > MaxSqlChars)
            entry.Sql = entry.Sql[..MaxSqlChars];
        while (all.Count > MaxEntries) all.RemoveAt(all.Count - 1);
        Save(all);
        return all;
    }

    public void Clear() => Save([]);

    private void Save(List<QueryHistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(QueryHistoryService), ex, "Query history could not be saved");
        }
    }
}
