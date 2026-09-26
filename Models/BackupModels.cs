namespace SchemaCompare.Models;

/// <summary>
/// A native T-SQL backup request. Unlike a BACPAC export the file is written by
/// the SQL Server service, so <see cref="FilePath"/> must be reachable from that
/// machine, not from this desktop.
/// </summary>
public sealed class BackupRequest
{
    public required string Database { get; init; }

    /// <summary>Path on the server host, e.g. D:\SqlBackups\Orders_20260925.bak.</summary>
    public required string FilePath { get; init; }

    /// <summary>Full database or transaction log.</summary>
    public bool LogBackup { get; init; }

    /// <summary>Excludes this backup from the recovery chain. A copy-only log backup
    /// does not truncate the transaction log.</summary>
    public bool CopyOnly { get; init; }

    public bool Checksum { get; init; } = true;

    public bool Compress { get; init; } = true;

    /// <summary>Overwrite the existing media file. False appends a new backup set.</summary>
    public bool OverwriteMedia { get; init; }

    /// <summary>Run RESTORE VERIFYONLY against the file once the backup finishes.</summary>
    public bool VerifyAfterBackup { get; init; } = true;

    /// <summary>Progress reporting granularity as a percentage of work.</summary>
    public int StatsPercent { get; init; } = 10;

    public string? Description { get; init; }

    public const int MinStatsPercent = 1;
    public const int MaxStatsPercent = 25;

    /// <summary>Throws when the request would fail on the server or surprise the operator.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
            throw new InvalidOperationException("Choose the database to back up.");
        if (string.IsNullOrWhiteSpace(FilePath))
            throw new InvalidOperationException("Enter the backup file path on the server.");
        if (FilePath.Contains('\0') || System.IO.Path.GetInvalidPathChars().Any(FilePath.Contains))
            throw new InvalidOperationException("The backup file path contains invalid characters.");
        if (StatsPercent is < MinStatsPercent or > MaxStatsPercent)
            throw new InvalidOperationException($"STATS must be between {MinStatsPercent} and {MaxStatsPercent} percent.");
    }
}
