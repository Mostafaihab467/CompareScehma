namespace SchemaCompare.Models;

/// <summary>
/// A source/target pairing the operator ran more than once: the weekly drift check between a
/// live database and a captured baseline, or staging against production.
///
/// A side is one of three things: a snapshot file, a saved connection profile (by id), or a
/// server and database typed in. The first two are references; the third stores only the two
/// names, which is why nothing here can hold a credential — a profile keeps its own
/// DPAPI-protected password, and a side that needs SQL or Entra authentication has to be saved
/// as a profile before it can take part in a comparison.
///
/// The labels are resolved when the pairing is saved, so a row draws without opening the profile
/// store or a package, and reads the same after the app restarts.
/// </summary>
public sealed class SavedComparison
{
    public string Name { get; init; } = string.Empty;

    public string? SourceProfileId { get; init; }
    public string? SourceSnapshotPath { get; init; }
    public string? SourceServer { get; init; }
    public string? SourceDatabase { get; init; }
    public string? SourceLabel { get; init; }

    public string? TargetProfileId { get; init; }
    public string? TargetSnapshotPath { get; init; }
    public string? TargetServer { get; init; }
    public string? TargetDatabase { get; init; }
    public string? TargetLabel { get; init; }

    /// <summary>Carried with the pairing because they change what a script is allowed to do, so
    /// re-running a comparison must not quietly widen the guard that was on it.</summary>
    public bool AllowUnsafeDrops { get; init; }
    public bool AllowUnsafeChanges { get; init; }

    public DateTimeOffset SavedAt { get; init; }

    /// <summary>True when a side points at a file rather than a server.</summary>
    public bool SourceIsSnapshot => !string.IsNullOrWhiteSpace(SourceSnapshotPath);
    public bool TargetIsSnapshot => !string.IsNullOrWhiteSpace(TargetSnapshotPath);

    /// <summary>"Nightly drift — localhost/EgyptMart ⇄ EgyptMart-20260926-0715.dacpac".</summary>
    public string Display => $"{Name} — {SourceLabel} ⇄ {TargetLabel}";

    public static string DescribeSide(bool isSnapshot, string? snapshotPath, string? server, string? database)
        => isSnapshot
            ? System.IO.Path.GetFileName(snapshotPath ?? "")
            : $"{server}/{database}";
}
