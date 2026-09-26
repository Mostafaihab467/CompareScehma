namespace SchemaCompare.Models;

/// <summary>
/// One snapshot file the operator has captured or used, as the library remembers it.
///
/// The provenance is copied out of the package at capture time rather than re-read every
/// visit: a list of twenty baselines should not open twenty .dacpac files just to draw a
/// row. The file inside the package stays the authority — this is a shortcut to it, and an
/// entry whose file has gone is dropped when the list loads.
/// </summary>
public sealed class SnapshotEntry
{
    public string Path { get; init; } = string.Empty;
    public string? CapturedServer { get; init; }
    public string? CapturedDatabase { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public long FileSizeBytes { get; init; }

    public static SnapshotEntry From(SnapshotInfo info) => new()
    {
        Path = info.Path,
        CapturedServer = info.CapturedServer,
        CapturedDatabase = info.CapturedDatabase,
        CapturedAt = info.CapturedAt,
        FileSizeBytes = info.FileSizeBytes,
    };

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>True when re-reading the file says the same thing, so the library keeps its
    /// existing row instead of churning the list a dropdown is bound to.</summary>
    public bool SameProvenanceAs(SnapshotEntry other) =>
        string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase)
        && CapturedServer == other.CapturedServer
        && CapturedDatabase == other.CapturedDatabase
        && CapturedAt == other.CapturedAt
        && FileSizeBytes == other.FileSizeBytes;

    /// <summary>The row text: which file, from which server, how old. A foreign .dacpac says
    /// so instead of showing a provenance this app never recorded.</summary>
    public string Display
    {
        get
        {
            var origin = CapturedServer is null
                ? CapturedDatabase ?? "captured by another tool"
                : $"{CapturedServer}/{CapturedDatabase}";
            var age = SnapshotInfo.DescribeAge(CapturedAt);
            return age.Length == 0 ? $"{FileName} — {origin}" : $"{FileName} — {origin}, {age}";
        }
    }
}
