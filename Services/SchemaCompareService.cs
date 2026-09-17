using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac.Compare;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

public class SchemaCompareService
{
    public async Task<(List<SchemaDiffItem> Items, CompareResultSummary Summary)> CompareAsync(
        ConnectionInfo sourceInfo, ConnectionInfo targetInfo, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var sourceCs = sourceInfo.ConnectionString;
            var targetCs = targetInfo.ConnectionString;

            progress?.Report($"Source: {sourceInfo.Server}/{sourceInfo.Database}");
            progress?.Report($"Target: {targetInfo.Server}/{targetInfo.Database}");
            progress?.Report("Loading source schema...");
            var sourceEndpoint = new SchemaCompareDatabaseEndpoint(sourceCs);
            ct.ThrowIfCancellationRequested();

            progress?.Report("Loading target schema...");
            var targetEndpoint = new SchemaCompareDatabaseEndpoint(targetCs);
            ct.ThrowIfCancellationRequested();

            progress?.Report("Building comparison...");
            var comparison = new SchemaComparison(sourceEndpoint, targetEndpoint);

            // Ensure all object types are compared; defaults can silently exclude some.
            comparison.Options.IgnoreAnsiNulls  = false;
            comparison.Options.IgnoreComments   = false;
            comparison.Options.IgnoreWhitespace = true;
            comparison.Options.DropObjectsNotInSource = false;

            ct.ThrowIfCancellationRequested();
            progress?.Report("Running comparison (this may take a moment)...");
            var result = comparison.Compare();
            ct.ThrowIfCancellationRequested();

            if (result == null)
                throw new InvalidOperationException("Comparison engine returned null result.");

            var items = new List<SchemaDiffItem>();
            var processed = 0;

            foreach (var diff in result.Differences)
            {
                ct.ThrowIfCancellationRequested();
                if (diff == null) continue;
                processed++;

                var status = diff.UpdateAction switch
                {
                    SchemaUpdateAction.Add    => DiffStatus.Added,
                    SchemaUpdateAction.Change => DiffStatus.Changed,
                    SchemaUpdateAction.Delete => DiffStatus.Deleted,
                    _                        => DiffStatus.Changed
                };

                var objName = diff.Name ?? "Unknown";
                var objType = diff.SourceObject?.ObjectType?.Name
                           ?? diff.TargetObject?.ObjectType?.Name
                           ?? "Unknown";

                // Guard script extraction: a failure here must never hide a valid diff.
                string sourceScript = string.Empty, targetScript = string.Empty;
                try { sourceScript = result.GetDiffEntrySourceScript(diff) ?? string.Empty; } catch { }
                try { targetScript = result.GetDiffEntryTargetScript(diff) ?? string.Empty; } catch { }

                items.Add(new SchemaDiffItem
                {
                    ObjectName   = objName,
                    ObjectType   = objType,
                    Status       = status,
                    SourceScript = sourceScript,
                    TargetScript = targetScript,
                });

                if (processed <= 5 || processed % 20 == 0)
                    progress?.Report($"  [{status}] {objType}: {objName}");
            }

            progress?.Report($"Found {processed} differences.");

            var summary = new CompareResultSummary
            {
                AddedCount   = items.Count(i => i.Status == DiffStatus.Added),
                ChangedCount = items.Count(i => i.Status == DiffStatus.Changed),
                DeletedCount = items.Count(i => i.Status == DiffStatus.Deleted),
            };

            progress?.Report(summary.TotalDifferences > 0
                ? $"Done — +{summary.AddedCount} added, ~{summary.ChangedCount} changed, -{summary.DeletedCount} deleted."
                : "No differences found. Databases are identical.");

            return (items, summary);
        }, ct);
    }

    public async Task<string> TestConnectionAsync(ConnectionInfo info, int timeoutSeconds = 8)
    {
        var testCs = info.BuildTestConnectionString(timeoutSeconds);
        return await Task.Run(async () =>
        {
            try
            {
                using var conn = new SqlConnection(testCs);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DB_NAME(), @@SERVERNAME";
                using var rdr = await cmd.ExecuteReaderAsync();
                string dbName = "", serverName = "";
                if (await rdr.ReadAsync())
                {
                    dbName = rdr.IsDBNull(0) ? "?" : rdr.GetString(0);
                    serverName = rdr.IsDBNull(1) ? "?" : rdr.GetString(1);
                }
                return $"Connected — Server: {serverName}, Database: {dbName}";
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException($"SQL connection failed: {ex.Message} (Code {ex.Number})", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Connection failed: {ex.Message}", ex);
            }
        });
    }

    public async Task<string> GenerateScriptAsync(ConnectionInfo sourceInfo, ConnectionInfo targetInfo)
    {
        return await Task.Run(() =>
        {
            var comparison = new SchemaComparison(
                new SchemaCompareDatabaseEndpoint(sourceInfo.ConnectionString),
                new SchemaCompareDatabaseEndpoint(targetInfo.ConnectionString));
            comparison.Options.IgnoreAnsiNulls  = false;
            comparison.Options.IgnoreComments   = false;
            comparison.Options.IgnoreWhitespace = true;
            comparison.Options.DropObjectsNotInSource = false;

            var result = comparison.Compare();
            if (result == null) throw new InvalidOperationException("Comparison returned no result.");
            var scriptResult = result.GenerateScript(targetInfo.Database);
            if (!scriptResult.Success)
                throw new InvalidOperationException(scriptResult.Message ?? scriptResult.Exception?.Message ?? "Script generation failed");

            return ReorderScriptBatches(scriptResult.Script, targetInfo.Database);
        });
    }

    public async Task<(bool Success, string Script)> ApplyChangesAsync(
        ConnectionInfo sourceInfo, ConnectionInfo targetInfo, IProgress<string>? progress = null,
        List<SchemaDiffItem>? includedItems = null)

    {
        return await Task.Run(async () =>
        {
            progress?.Report("Comparing schemas...");
            var comparison = new SchemaComparison(
                new SchemaCompareDatabaseEndpoint(sourceInfo.ConnectionString),
                new SchemaCompareDatabaseEndpoint(targetInfo.ConnectionString));
            comparison.Options.IgnoreAnsiNulls  = false;
            comparison.Options.IgnoreComments   = false;
            comparison.Options.IgnoreWhitespace = true;
            comparison.Options.DropObjectsNotInSource = false;

            var result = comparison.Compare();
            if (result == null) throw new InvalidOperationException("Comparison returned no result.");

            // If caller specified which items to apply, exclude everything else from the DacFx result
            if (includedItems != null && includedItems.Count > 0)
            {
                // Build a lookup of selected object names (lower-case for case-insensitive match)
                var selectedNames = includedItems
                    .Select(i => i.ObjectName.ToLowerInvariant())
                    .ToHashSet();

                foreach (var diff in result.Differences)
                {
                    if (diff == null) continue;
                    var name = (diff.Name ?? string.Empty).ToLowerInvariant();
                    if (!selectedNames.Contains(name))
                        result.Exclude(diff);
                }
                progress?.Report($"Filtered to {selectedNames.Count} selected item(s).");
            }

            progress?.Report("Generating deployment script...");
            var scriptResult = result.GenerateScript(targetInfo.Database);
            if (!scriptResult.Success)
                throw new InvalidOperationException(scriptResult.Message ?? scriptResult.Exception?.Message ?? "Script generation failed");

            var reorderedScript = ReorderScriptBatches(scriptResult.Script, targetInfo.Database);
            var batches = ExtractExecutableBatches(reorderedScript);

            progress?.Report($"Applying {batches.Count} changes to target database...");

            using var conn = new SqlConnection(targetInfo.ConnectionString);
            await conn.OpenAsync();

            var deferredBatches = new List<string>();
            var pass1Errors = new List<string>();
            int executedCount = 0;

            // Pass 1: Execute all batches in reordered sequence (Functions before Views, Triggers after Views/Procs)
            for (int i = 0; i < batches.Count; i++)
            {
                var batch = batches[i];
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = batch;
                    cmd.CommandTimeout = 120;
                    await cmd.ExecuteNonQueryAsync();
                    executedCount++;

                    var summaryMatch = Regex.Match(batch, @"(?:PRINT\s+N'(Creating\s+[^.']+)|CREATE\s+(TABLE|VIEW|FUNCTION|PROCEDURE|INDEX|TRIGGER|USER|ROLE)\s+([^\s\r\n(]+))", RegexOptions.IgnoreCase);
                    if (summaryMatch.Success)
                    {
                        var actionDesc = !string.IsNullOrEmpty(summaryMatch.Groups[1].Value)
                            ? summaryMatch.Groups[1].Value
                            : $"Creating {summaryMatch.Groups[2].Value} {summaryMatch.Groups[3].Value}";
                        if (executedCount <= 5 || executedCount % 10 == 0 || executedCount == batches.Count)
                            progress?.Report($"[{executedCount}/{batches.Count}] {actionDesc}");
                    }
                }
                catch (SqlException ex)
                {
                    // Defer if this batch may have an unresolved view/proc dependency to be resolved in Pass 2
                    deferredBatches.Add(batch);
                    pass1Errors.Add($"Batch error (will retry): {ex.Message}");
                }
            }

            // Pass 2: Retry any deferred batches that failed in Pass 1
            if (deferredBatches.Count > 0)
            {
                progress?.Report($"Retrying {deferredBatches.Count} deferred items in Pass 2...");
                var stillFailed = new List<string>();
                var retryErrors = new List<string>();

                foreach (var batch in deferredBatches)
                {
                    try
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = batch;
                        cmd.CommandTimeout = 120;
                        await cmd.ExecuteNonQueryAsync();
                        progress?.Report("Resolved deferred dependency item successfully.");
                    }
                    catch (SqlException ex)
                    {
                        stillFailed.Add(batch);
                        var snippet = batch.Length > 250 ? batch[..250] + "..." : batch;
                        retryErrors.Add($"{ex.Message} (Error #{ex.Number})\nSQL: {snippet}");
                    }
                }

                if (stillFailed.Count > 0)
                {
                    throw new InvalidOperationException($"Deploy failed for {stillFailed.Count} item(s):\n\n" + string.Join("\n\n", retryErrors));
                }
            }

            progress?.Report("Changes applied successfully.");
            return (true, reorderedScript);
        });
    }

    /// <summary>
    /// Reorders DacFx deployment script so that User-Defined Functions are created BEFORE Views.
    /// In default DacFx ordering, Views are placed before Functions, causing SQL Server error 4121
    /// (Cannot find user-defined function) when deploying views that call scalar functions on an empty database.
    /// </summary>
    public static string ReorderScriptBatches(string sqlScript, string targetDatabase)
    {
        if (string.IsNullOrWhiteSpace(sqlScript)) return sqlScript;

        if (!string.IsNullOrWhiteSpace(targetDatabase))
        {
            sqlScript = Regex.Replace(sqlScript, @"\$\(DatabaseName\)", targetDatabase, RegexOptions.IgnoreCase);
        }

        var rawBatches = Regex.Split(sqlScript, @"(?<=[\r\n])\s*GO\s*(?=[\r\n]|$)", RegexOptions.IgnoreCase);

        var preBatches = new List<string>();      // Headers, Users, Roles, Tables, Constraints, Types, Sequences
        var funcBatches = new List<string>();     // Scalar & Table-Valued Functions
        var viewBatches = new List<string>();     // Views
        var postBatches = new List<string>();     // Procedures, Triggers, Extended Properties, Post-deploy

        bool seenViewOrFunc = false;

        foreach (var b in rawBatches)
        {
            var trimmed = b.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            bool isFunc = Regex.IsMatch(trimmed, @"(?:PRINT\s+N'Creating\s+Function|CREATE\s+(?:OR\s+ALTER\s+)?FUNCTION)", RegexOptions.IgnoreCase);
            bool isView = Regex.IsMatch(trimmed, @"(?:PRINT\s+N'Creating\s+View|CREATE\s+(?:OR\s+ALTER\s+)?VIEW)", RegexOptions.IgnoreCase);
            bool isProc = Regex.IsMatch(trimmed, @"(?:PRINT\s+N'Creating\s+Procedure|CREATE\s+(?:OR\s+ALTER\s+)?PROCEDURE|CREATE\s+procedure)", RegexOptions.IgnoreCase);

            if (isFunc)
            {
                seenViewOrFunc = true;
                funcBatches.Add(trimmed);
            }
            else if (isView)
            {
                seenViewOrFunc = true;
                viewBatches.Add(trimmed);
            }
            else if (isProc || (seenViewOrFunc && (trimmed.Contains("Extended Property", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("#__checkStatus", StringComparison.OrdinalIgnoreCase))))
            {
                postBatches.Add(trimmed);
            }
            else
            {
                if (!seenViewOrFunc)
                    preBatches.Add(trimmed);
                else
                    postBatches.Add(trimmed);
            }
        }

        var reordered = new List<string>();
        reordered.AddRange(preBatches);
        reordered.AddRange(funcBatches); // Functions BEFORE Views!
        reordered.AddRange(viewBatches);
        reordered.AddRange(postBatches);

        return string.Join("\r\nGO\r\n", reordered) + "\r\nGO\r\n";
    }

    /// <summary>
    /// Cleans and extracts executable T-SQL batches from a deployment script for ADO.NET execution.
    /// Strips SQLCMD directives (:setvar, :on error) and skips client-side validation guards.
    /// </summary>
    public static List<string> ExtractExecutableBatches(string reorderedScript)
    {
        var rawBatches = Regex.Split(reorderedScript, @"(?<=[\r\n])\s*GO\s*(?=[\r\n]|$)", RegexOptions.IgnoreCase);
        var executableBatches = new List<string>();

        foreach (var b in rawBatches)
        {
            var trimmed = b.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Strip sqlcmd commands (:setvar, :on error, :r, etc.)
            var cleaned = Regex.Replace(trimmed, @"^\s*:(?:setvar|on error|r)\s+.*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(cleaned)) continue;

            // Skip sqlcmd mode verification guards and USE master/NOEXEC directives
            if (Regex.IsMatch(cleaned, @"IF\s+N'\$\(__IsSqlCmdEnabled\)'\s+NOT\s+LIKE\s+N'True'", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(cleaned, @"^SET\s+NOEXEC\s+(?:ON|OFF)", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(cleaned, @"^USE\s+\[?master\]?", RegexOptions.IgnoreCase))
            {
                continue;
            }

            executableBatches.Add(cleaned);
        }

        return executableBatches;
    }
}
