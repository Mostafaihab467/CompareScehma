namespace SchemaCompare.Models;

/// <summary>
/// One side of a schema comparison: either a live database to read or a snapshot file
/// captured earlier. The compare service takes these instead of connections so that
/// "compare two servers", "what changed since the baseline" and "diff two files" all run
/// through one code path — and so that nothing downstream has to assume a reachable server.
/// </summary>
public class SchemaSource
{
    /// <summary>Set for a live database; null for a snapshot.</summary>
    public ConnectionInfo? Connection { get; init; }

    /// <summary>Set for a snapshot file; empty for a live database.</summary>
    public string SnapshotPath { get; init; } = string.Empty;

    /// <summary>Provenance read from the file, when this side is a snapshot.</summary>
    public SnapshotInfo? Snapshot { get; init; }

    public bool IsSnapshot => !string.IsNullOrWhiteSpace(SnapshotPath);

    public static SchemaSource OfDatabase(ConnectionInfo info) => new() { Connection = info };

    public static SchemaSource OfSnapshot(string path, SnapshotInfo? metadata = null) =>
        new() { SnapshotPath = path, Snapshot = metadata };

    /// <summary>
    /// The database a script generated against this side must USE. A snapshot has no live
    /// database of its own, so the name it was captured from stands in — that script is a
    /// preview of a change to make somewhere else, never something to run as-is.
    /// </summary>
    public string? DatabaseName => IsSnapshot ? Snapshot?.CapturedDatabase : Connection?.Database;

    /// <summary>What the progress lines and the generated script headers call this side. Kept
    /// compact and free of credentials: a script header is copied into tickets and chat.</summary>
    public string DisplayName => IsSnapshot
        ? $"snapshot [{System.IO.Path.GetFileName(SnapshotPath)}]"
        : $"{Connection?.Server}/{Connection?.Database}";
}
