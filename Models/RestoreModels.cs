namespace SchemaCompare.Models;

/// <summary>
/// One backup set inside a media file, as reported by RESTORE HEADERONLY.
/// A single .bak can hold many sets (full + differentials + logs).
/// </summary>
public sealed class BackupSetInfo
{
    /// <summary>1-based set number passed to RESTORE ... WITH FILE = n.</summary>
    public int Position { get; init; }

    /// <summary>SQL Server backup type code: D full, I differential, L log, F file, G diff file, P partial, Q partial diff.</summary>
    public char BackupType { get; init; }

    public string DatabaseName { get; init; } = "";
    public DateTime BackupStartDate { get; init; }
    public long BackupSizeBytes { get; init; }
    public string ServerName { get; init; } = "";
    public bool IsCopyOnly { get; init; }
    public string Description { get; init; } = "";

    public string TypeText => BackupType switch
    {
        'D' => "Full",
        'I' => "Differential",
        'L' => "Transaction log",
        'F' => "File or filegroup",
        'G' => "Differential file",
        'P' => "Partial",
        'Q' => "Partial differential",
        _ => "Unknown type"
    };

    public string SizeText => BackupSizeBytes <= 0
        ? "unknown size"
        : $"{BackupSizeBytes / 1048576.0:N0} MB";

    public string ListLabel =>
        $"#{Position} · {TypeText} · {BackupStartDate:yyyy-MM-dd HH:mm:ss} · {SizeText}" +
        (IsCopyOnly ? " · copy-only" : "");
}

/// <summary>
/// One file inside a backup set, as reported by RESTORE FILELISTONLY. Its
/// PhysicalName is where the backup was taken from, which is usually not where
/// this instance should write the restored file.
/// </summary>
public sealed class BackupFileInfo
{
    public string LogicalName { get; init; } = "";
    public string PhysicalName { get; init; } = "";

    /// <summary>D data, L log, N filestream, F fulltext.</summary>
    public char Type { get; init; }

    public long SizeBytes { get; init; }
    public long MaxSizeBytes { get; init; }
    public long GrowthBytes { get; init; }
    public string FileGroupName { get; init; } = "";

    public bool IsLog => Type == 'L';

    public string TypeText => IsLog ? "Log" : "Rows";

    public string SizeText => SizeBytes <= 0 ? "unknown" : $"{SizeBytes / 1048576.0:N0} MB";

    /// <summary>File name used when relocating into a fresh directory.</summary>
    public string FileName => Path.GetFileName(PhysicalName);
}

/// <summary>
/// Everything needed to emit and run one RESTORE. Built by the restore dialog,
/// consumed by <see cref="Services.ManagerScriptBuilder.RestoreDatabase"/> so the
/// script the user previews is exactly the script that runs.
/// </summary>
public sealed class RestorePlan
{
    public required string BackupPath { get; init; }

    /// <summary>Backup set number within the file (RESTORE ... WITH FILE).</summary>
    public int SetPosition { get; set; } = 1;

    /// <summary>Database name the restore creates or overwrites.</summary>
    public required string TargetDatabase { get; set; }

    /// <summary>Database name the backup was taken from.</summary>
    public string? SourceDatabase { get; set; }

    /// <summary>Files in the selected set, from FILELISTONLY.</summary>
    public List<BackupFileInfo> Files { get; } = [];

    /// <summary>Logical name to new physical path; only relocated files are moved.</summary>
    public Dictionary<string, string> Moves { get; } = [];

    /// <summary>Point-in-time recovery target; null restores the end of the backup.</summary>
    public DateTime? StopAt { get; set; }

    /// <summary>Allow overwriting an existing database. Deliberately opt-in.</summary>
    public bool ReplaceExisting { get; set; }

    /// <summary>Leave the database restoring so more backups can follow; false = RECOVERY.</summary>
    public bool NoRecovery { get; set; }

    public bool IsInPlaceRestore =>
        string.Equals(TargetDatabase, SourceDatabase, StringComparison.OrdinalIgnoreCase);

    public int RelocatedCount => Moves.Count;

    /// <summary>Throws when the plan would silently damage existing data.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TargetDatabase))
            throw new InvalidOperationException("Enter the database name to restore.");
        if (string.IsNullOrWhiteSpace(BackupPath))
            throw new InvalidOperationException("Choose a backup file first.");
        if (SetPosition < 1)
            throw new InvalidOperationException("Select the backup set to restore.");
        if (Path.GetInvalidPathChars().Any(BackupPath.Contains) || BackupPath.Contains('\0'))
            throw new InvalidOperationException("The backup file path contains invalid characters.");
        if (StopAt is { } stopAt && stopAt > DateTime.Now)
            throw new InvalidOperationException("STOPAT cannot point in the future.");

        foreach (var (logical, physical) in Moves)
        {
            if (string.IsNullOrWhiteSpace(physical))
                throw new InvalidOperationException($"Enter a destination file for '{logical}'.");

            var file = Files.FirstOrDefault(
                f => string.Equals(f.LogicalName, logical, StringComparison.OrdinalIgnoreCase));
            var extension = Path.GetExtension(physical);
            if (file != null && !string.IsNullOrEmpty(extension))
            {
                var looksLikeLog = extension.Equals(".ldf", StringComparison.OrdinalIgnoreCase);
                if (looksLikeLog != file.IsLog)
                    throw new InvalidOperationException(
                        $"'{logical}' is a {(file.IsLog ? "log" : "data")} file — " +
                        $"restore it to a {(file.IsLog ? ".ldf" : ".mdf")} path.");
            }
        }

        var destinations = Moves.Values
            .Select(v => Path.GetFullPath(v).ToUpperInvariant())
            .ToList();
        var clash = destinations.FirstOrDefault(d => destinations.Count(x => x == d) > 1);
        if (clash != null)
            throw new InvalidOperationException($"Two files would be restored onto the same path: {clash}");
    }
}
