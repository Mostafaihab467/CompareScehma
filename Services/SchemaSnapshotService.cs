using System.Text.RegularExpressions;
using Microsoft.SqlServer.Dac;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Writes and reads schema snapshots as .dacpac files — a schema-only copy of a database
/// that can be compared later without the server being reachable. This is the portable half
/// of a migration workflow: capture the target before deploying and the baseline outlives
/// the instance it came from, and comparing that file against the live database again is
/// how drift gets found.
/// </summary>
public sealed class SchemaSnapshotService
{
    public const string Extension = ".dacpac";

    /// <summary>Written into the package description so a file can be identified by hand
    /// and by this app. Anything without it is treated as a foreign .dacpac.</summary>
    private const string Marker = "SchemaCompare snapshot";

    private static readonly Regex CapturedPattern =
        new(@"SchemaCompare snapshot from (?<server>.+?) at (?<when>\d{4}-\d{2}-\d{2}T[\d:.]+(?:Z|[+-]\d{2}:\d{2}))",
            RegexOptions.Compiled);

    /// <summary>The folder suggested the first time a snapshot is captured.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SchemaCompare Snapshots");

    /// <summary>"EgyptMart-20260926-0715.dacpac". The timestamp is UTC so captures of the same
    /// day sort in order and a second capture never silently replaces the first.</summary>
    public static string SuggestedFileName(string database, DateTimeOffset capturedAt)
    {
        var safe = (database ?? "").Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(safe)) safe = "snapshot";
        return $"{safe}-{capturedAt.ToUniversalTime():yyyyMMdd-HHmm}{Extension}";
    }

    /// <summary>
    /// Exports one database's schema to a file. No data, no logins and no permissions are
    /// included: those differ between every pair of servers and would bury the schema drift
    /// the file exists to show. Returns the metadata read back out of the finished package,
    /// so the caller reports what actually landed rather than what was intended.
    /// </summary>
    public async Task<SnapshotInfo> CaptureAsync(
        ConnectionInfo database, string filePath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(database.Server) || string.IsNullOrWhiteSpace(database.Database))
            throw new InvalidOperationException("Choose a live database to capture before saving a snapshot.");
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("No file name was given for the snapshot.");
        if (!string.Equals(Path.GetExtension(filePath), Extension, StringComparison.OrdinalIgnoreCase))
            filePath += Extension;

        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? DefaultDirectory);

        var capturedAt = DateTimeOffset.UtcNow;
        // Extract writes the description into the package, and ReadSnapshot is the only place
        // that parses it back — so the two cannot drift apart.
        var description = $"{Marker} from {database.Server} at {capturedAt:O}";

        progress?.Report($"Capturing the schema of {database.SafeForLog}...");
        var target = filePath;
        await Task.Run(() =>
        {
            var dac = new DacServices(database.ConnectionString);
            // Extraction repeats the same per-object lines dozens of times; the log only needs each once.
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dac.Message += (_, e) =>
            {
                var text = e.Message.Message;
                if (!string.IsNullOrWhiteSpace(text) && reported.Add(text))
                    progress?.Report(text);
            };

            var options = new DacExtractOptions
            {
                VerifyExtraction = true,
                ExtractAllTableData = false,
                IgnorePermissions = true,
                IgnoreUserLoginMappings = true,
            };
            try
            {
                dac.Extract(target, database.Database, database.Database, new Version(1, 0, 0, 0),
                    description, null, options, ct);
            }
            catch (Exception ex) when (DatabaseBackupService.IsUnresolvedReferenceError(ex))
            {
                // Same DAC quirk as a BACPAC export: a three-part name like [ThisDb].[dbo].[Table]
                // reads as an external reference, so the package is fine and only the check fails.
                progress?.Report("The schema uses three-part names (SQL71562), so verification cannot resolve them. Re-extracting without verification...");
                options.VerifyExtraction = false;
                dac.Extract(target, database.Database, database.Database, new Version(1, 0, 0, 0),
                    description, null, options, ct);
            }
        }, ct).ConfigureAwait(false);

        var info = ReadSnapshot(target);
        progress?.Report($"Snapshot written to {target}.");
        return info;
    }

    /// <summary>
    /// Reads the provenance stored inside a snapshot file. Loads the package header only, so
    /// this stays fast whatever the schema size, and reports a foreign .dacpac by name rather
    /// than inventing a capture date for it.
    /// </summary>
    public static SnapshotInfo ReadSnapshot(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("No snapshot file selected.");
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"The snapshot file could not be found: {Path.GetFileName(filePath)}", filePath);

        using var package = DacPackage.Load(filePath);
        var description = package.Description ?? string.Empty;
        var match = CapturedPattern.Match(description);
        var size = new FileInfo(filePath).Length;

        return new SnapshotInfo
        {
            Path = filePath,
            FileName = Path.GetFileNameWithoutExtension(filePath),
            CapturedDatabase = string.IsNullOrWhiteSpace(package.Name) ? null : package.Name,
            CapturedServer = match.Success ? match.Groups["server"].Value : null,
            CapturedAt = match.Success && DateTimeOffset.TryParse(match.Groups["when"].Value, out var when) ? when : null,
            FileSizeBytes = size,
        };
    }
}
