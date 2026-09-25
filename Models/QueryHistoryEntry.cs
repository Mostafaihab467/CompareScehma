using System.Text.Json.Serialization;

namespace SchemaCompare.Models;

/// <summary>
/// One executed script or selection, kept in the persistent query history so the
/// user can search previous work and drop it back into the editor.
/// </summary>
public sealed class QueryHistoryEntry
{
    public string Sql { get; set; } = string.Empty;

    /// <summary>Local wall-clock time of the run — history is read by people, not machines.</summary>
    public DateTime ExecutedAt { get; set; }

    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public int ResultSets { get; set; }
    public int TotalRows { get; set; }

    /// <summary>"script" or "selection".</summary>
    public string Scope { get; set; } = "script";

    /// <summary>First line of the error when the run failed; null when it succeeded.</summary>
    public string? Error { get; set; }

    [JsonIgnore]
    public string TimeText => ExecutedAt.ToString("yyyy-MM-dd HH:mm:ss");

    [JsonIgnore]
    public string TargetText => string.IsNullOrWhiteSpace(Database) ? Server : $"{Server} · {Database}";

    [JsonIgnore]
    public bool Failed => !string.IsNullOrEmpty(Error);

    [JsonIgnore]
    public string OutcomeText => Failed
        ? $"error — {Error}"
        : $"{ResultSets} result set(s), {TotalRows:N0} row(s), {DurationSeconds:0.00}s";

    /// <summary>Single-line summary for the history list.</summary>
    [JsonIgnore]
    public string Preview
    {
        get
        {
            var flat = string.Join(' ', Sql.Split('\n').Select(l => l.Trim()));
            flat = System.Text.RegularExpressions.Regex.Replace(flat, @"\s{2,}", " ").Trim();
            return flat.Length <= 150 ? flat : flat[..150] + "…";
        }
    }

    public bool Matches(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var needle = filter.Trim();
        return Sql.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               Server.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               Database.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               (Error?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
