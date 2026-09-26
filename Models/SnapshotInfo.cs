namespace SchemaCompare.Models;

/// <summary>
/// What a <see cref="SchemaSource"/> that points at a snapshot file knows about itself.
/// The capture provenance is stored inside the package — DacFx keeps the application name
/// and description as package properties — so it travels with the file and survives a move
/// to another machine; no sidecar file has to be kept beside it.
///
/// A .dacpac that came from somewhere else (SSDT, a build pipeline) carries text we did not
/// write. Those values are then shown as they are and <see cref="CapturedServer"/> stays
/// null, rather than being guessed at.
/// </summary>
public class SnapshotInfo
{
    public string Path { get; init; } = string.Empty;

    /// <summary>File name without the extension — the name the operator gave the baseline.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Database the snapshot was taken from, as recorded in the package.</summary>
    public string? CapturedDatabase { get; init; }

    /// <summary>Server it was taken from, when the package says so.</summary>
    public string? CapturedServer { get; init; }

    /// <summary>When it was taken, when the package says so.</summary>
    public DateTimeOffset? CapturedAt { get; init; }

    public long FileSizeBytes { get; init; }

    /// <summary>One line for the compare cards: where the baseline came from and when.</summary>
    public string Caption
    {
        get
        {
            if (CapturedAt is null && CapturedServer is null)
                return CapturedDatabase is null ? "Captured by another tool." : $"Captured from {CapturedDatabase}.";
            var from = CapturedServer is null ? CapturedDatabase : $"{CapturedServer}/{CapturedDatabase}";
            var when = CapturedAt?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") ?? "unknown time";
            return $"Captured from {from} at {when} UTC.";
        }
    }

    /// <summary>Age, so "compare against the baseline" never quietly means a two-year-old one.</summary>
    public string AgeCaption
    {
        get
        {
            if (CapturedAt is null) return string.Empty;
            var age = DateTimeOffset.UtcNow - CapturedAt.Value;
            if (age.TotalMinutes < 1) return "just now";
            if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
            if (age.TotalDays < 1) return $"{(int)age.TotalHours} h ago";
            if (age.TotalDays < 60) return $"{(int)age.TotalDays} days ago";
            return CapturedAt.Value.ToUniversalTime().ToString("yyyy-MM-dd") + " (UTC)";
        }
    }
}
