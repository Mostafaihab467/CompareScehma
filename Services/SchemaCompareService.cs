using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac.Compare;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

public class SchemaCompareService
{
    /// <summary>
    /// The DacFx endpoint for a side of the comparison: a live connection or a snapshot file.
    /// Everything else — options, difference walking, script generation — is identical, which
    /// is the point of taking <see cref="SchemaSource"/> instead of a connection.
    /// </summary>
    private static SchemaCompareEndpoint EndpointFor(SchemaSource side)
    {
        if (side.IsSnapshot)
        {
            if (!File.Exists(side.SnapshotPath))
                throw new FileNotFoundException($"The snapshot file could not be found: {Path.GetFileName(side.SnapshotPath)}", side.SnapshotPath);
            return new SchemaCompareDacpacEndpoint(side.SnapshotPath);
        }
        if (side.Connection is null)
            throw new InvalidOperationException("One side of the comparison has neither a database nor a snapshot file.");
        return new SchemaCompareDatabaseEndpoint(side.Connection.ConnectionString);
    }

    public async Task<(List<SchemaDiffItem> Items, CompareResultSummary Summary)> CompareAsync(
        SchemaSource source, SchemaSource target, IProgress<string>? progress = null,
        CancellationToken ct = default, bool allowUnsafeDrops = false, bool allowUnsafeChanges = false)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report($"Source: {source.DisplayName}");
            progress?.Report($"Target: {target.DisplayName}");
            progress?.Report("Loading source schema...");
            var sourceEndpoint = EndpointFor(source);
            ct.ThrowIfCancellationRequested();

            progress?.Report("Loading target schema...");
            var targetEndpoint = EndpointFor(target);
            ct.ThrowIfCancellationRequested();

            progress?.Report("Building comparison...");
            var comparison = new SchemaComparison(sourceEndpoint, targetEndpoint);

            comparison.Options.IgnoreAnsiNulls  = false;
            comparison.Options.IgnoreComments   = false;
            comparison.Options.IgnoreWhitespace = true;
            comparison.Options.DropObjectsNotInSource = allowUnsafeDrops;
            comparison.Options.BlockOnPossibleDataLoss = !allowUnsafeChanges;

            ct.ThrowIfCancellationRequested();
            progress?.Report(
                $"Comparing {source.DisplayName} against {target.DisplayName} with DacFx " +
                $"(drops {(allowUnsafeDrops ? "allowed" : "blocked")}, data loss {(allowUnsafeChanges ? "allowed" : "blocked")})...");
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
                await conn.OpenAsync().ConfigureAwait(false);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DB_NAME(), @@SERVERNAME";
                using var rdr = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                string dbName = "", serverName = "";
                if (await rdr.ReadAsync().ConfigureAwait(false))
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
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// UP script: makes <paramref name="target"/> look like <paramref name="source"/>.
    /// </summary>
    public Task<string> GenerateScriptAsync(SchemaSource source, SchemaSource target, bool allowUnsafeDrops = false, bool allowUnsafeChanges = false)
        => GenerateScriptBetweenAsync(source, target, ScriptDatabaseName(target, source), allowUnsafeDrops, allowUnsafeChanges);

    /// <summary>
    /// DOWN script: the same comparison read the other way round, so whatever the deploy
    /// creates this drops and whatever it drops this recreates — headed at the target, which
    /// is the database that was changed and therefore the one to run it against.
    ///
    /// It is only a rollback while the target still holds the state described here. Generate
    /// it *before* deploying: afterwards the target matches the source, the reversed diff is
    /// empty, and the undo window is gone. Reversing an <c>ALTER COLUMN</c> that widened or
    /// lengthened a column can also truncate live rows, so this text is a preview and a copy
    /// buffer, never something the app executes.
    ///
    /// There is deliberately no <c>allowUnsafeDrops</c> parameter: reversing the deploy means
    /// dropping the objects it added, so a rollback that honoured the drop guard would undo
    /// nothing. The data-loss guard still applies unless the operator lifted it for the deploy.
    /// </summary>
    public Task<string> GenerateRollbackScriptAsync(SchemaSource source, SchemaSource target, bool allowUnsafeChanges = false)
        => GenerateScriptBetweenAsync(target, source, ScriptDatabaseName(target, source), allowUnsafeDrops: true, allowUnsafeChanges,
            rollbackHeader: $"-- ROLLBACK (DOWN) for {target.DisplayName}\r\n" +
                            $"-- Reversed from the UP diff {source.DisplayName} -> {target.DisplayName}.\r\n" +
                            $"-- Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC and only valid while the target is still in its pre-deploy state.\r\n" +
                            "-- This script DROPS the objects the deploy added — that is its purpose. Reverting a widening\r\n" +
                            "-- change can also truncate data. Review before running.\r\n\r\n");

    /// <summary>
    /// Which database a generated script speaks about. The side being rewritten comes first; a
    /// snapshot has no live database, so it contributes the name it was captured from, and the
    /// other side is the last resort. A script whose target is a file is a preview to review,
    /// never something to press Run on, and the header says so.
    /// </summary>
    private static string ScriptDatabaseName(SchemaSource rewritten, SchemaSource other) =>
        rewritten.DatabaseName ?? other.DatabaseName ?? "the target database";

    /// <summary>
    /// Single source of truth for both directions: <paramref name="desired"/> is the DacFx
    /// "source" (the model to move towards) and <paramref name="current"/> the "target" (the
    /// database the script rewrites). Deploy and rollback differ only in which side is which,
    /// so the preview, the confirmation dialog and the executed batches cannot drift apart.
    /// </summary>
    private async Task<string> GenerateScriptBetweenAsync(
        SchemaSource desired, SchemaSource current, string deployDatabase,
        bool allowUnsafeDrops, bool allowUnsafeChanges, string rollbackHeader = "")
    {
        return await Task.Run(() =>
        {
            var comparison = new SchemaComparison(EndpointFor(desired), EndpointFor(current));
            comparison.Options.IgnoreAnsiNulls  = false;
            comparison.Options.IgnoreComments   = false;
            comparison.Options.IgnoreWhitespace = true;
            comparison.Options.DropObjectsNotInSource = allowUnsafeDrops;
            comparison.Options.BlockOnPossibleDataLoss = !allowUnsafeChanges;

            var result = comparison.Compare();
            if (result == null) throw new InvalidOperationException("Comparison returned no result.");
            if (!result.Differences.Any())
                return rollbackHeader + SnapshotNote(desired, current) + NothingToScript(deployDatabase, isRollback: rollbackHeader.Length > 0);
            var scriptResult = result.GenerateScript(deployDatabase);
            if (!scriptResult.Success)
                throw new InvalidOperationException(scriptResult.Message ?? scriptResult.Exception?.Message ?? "Script generation failed");

            return rollbackHeader + SnapshotNote(desired, current)
                   + RetargetScriptHeader(ReorderScriptBatches(scriptResult.Script, deployDatabase), deployDatabase);
        });
    }

    /// <summary>
    /// A snapshot on either side changes what the script means, and the diff itself cannot say
    /// so: "desired" being a file means the live database has objects the file never saw, and
    /// those arrive as DROPs. Better stated at the top than discovered in production.
    /// </summary>
    private static string SnapshotNote(SchemaSource desired, SchemaSource current)
    {
        if (current.IsSnapshot)
            return $"-- NOTE: the current state is the {current.DisplayName} file, so this script rewrites no\r\n" +
                   $"-- database as written. Check the USE line names the one you mean before running it.\r\n\r\n";
        if (desired.IsSnapshot)
            return $"-- NOTE: the desired state is the {desired.DisplayName} file. Anything the live database\r\n" +
                   $"-- gained after that snapshot was taken appears below as a DROP. Read those lines first.\r\n\r\n";
        return "";
    }

    /// <summary>
    /// DacFx refuses to generate a script from a comparison that found nothing — it throws
    /// "Performing script generation is not possible for this comparison result" — which the
    /// app used to surface as a red error for the entirely normal case of two databases that
    /// already match. An empty diff is an answer, not a failure.
    /// </summary>
    private static string NothingToScript(string deployDatabase, bool isRollback) =>
        $"-- {(isRollback ? "Rollback" : "Deployment")} script for {deployDatabase}\r\n" +
        $"-- The compared schemas match: there is nothing to {(isRollback ? "undo" : "deploy")}.\r\n";

    /// <summary>
    /// DacFx writes the database it modelled into the script's <c>:setvar</c> lines and its
    /// header comment, and <c>GenerateScript(name)</c> only rewrites the <c>USE</c> statement.
    /// Read forwards those agree by accident; reversed, the body would name the source while
    /// the <c>USE</c> named the target — so every script states in its own header the database
    /// it has to be run against.
    /// </summary>
    private static string RetargetScriptHeader(string script, string deployDatabase)
    {
        if (string.IsNullOrWhiteSpace(deployDatabase)) return script;
        script = Regex.Replace(script, @"(?<=:setvar\s+DatabaseName\s+"")[^""]*(?="")", _ => deployDatabase);
        script = Regex.Replace(script, @"(?<=:setvar\s+DefaultFilePrefix\s+"")[^""]*(?="")", _ => deployDatabase);
        script = Regex.Replace(script, @"(?<=Deployment script for )\S+", _ => deployDatabase);
        return script;
    }

    /// <summary>
    /// Deploys the comparison. Only the target has to be a live database — publishing a
    /// snapshot onto one is the normal dacpac workflow — and a snapshot target is refused
    /// rather than silently written to whichever server the file happens to name.
    /// </summary>
    public async Task<(bool Success, string Script)> ApplyChangesAsync(
        SchemaSource source, SchemaSource target, IProgress<string>? progress = null,
        List<SchemaDiffItem>? includedItems = null, bool allowUnsafeDrops = false, bool allowUnsafeChanges = false)

    {
        if (target.IsSnapshot)
            throw new InvalidOperationException("A snapshot file cannot be deployed to. Put the live database on the target side.");
        var targetConnection = target.Connection!;
        var targetDatabase = ScriptDatabaseName(target, source);

        return await Task.Run(async () =>
        {
            progress?.Report("Comparing schemas...");
            var comparison = new SchemaComparison(EndpointFor(source), EndpointFor(target));
            comparison.Options.IgnoreAnsiNulls  = false;
            comparison.Options.IgnoreComments   = false;
            comparison.Options.IgnoreWhitespace = true;
            comparison.Options.DropObjectsNotInSource = allowUnsafeDrops;
            comparison.Options.BlockOnPossibleDataLoss = !allowUnsafeChanges;

            var result = comparison.Compare();
            if (result == null) throw new InvalidOperationException("Comparison returned no result.");

            if (includedItems != null && includedItems.Count > 0)
            {
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
            var scriptResult = result.GenerateScript(targetDatabase);
            if (!scriptResult.Success)
                throw new InvalidOperationException(scriptResult.Message ?? scriptResult.Exception?.Message ?? "Script generation failed");

            var reorderedScript = ReorderScriptBatches(scriptResult.Script, targetDatabase);
            var batches = ExtractExecutableBatches(reorderedScript, allowDataLoss: allowUnsafeChanges);

            progress?.Report($"Applying {batches.Count} changes to target database...");

            using var conn = new SqlConnection(targetConnection.ConnectionString);
            await conn.OpenAsync().ConfigureAwait(false);

            var deferredBatches = new List<string>();
            var pass1Errors = new List<string>();
            int executedCount = 0;

            for (int i = 0; i < batches.Count; i++)
            {
                var batch = batches[i];
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = batch;
                    cmd.CommandTimeout = 120;
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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
                    deferredBatches.Add(batch);
                    pass1Errors.Add($"Batch error (will retry): {ex.Message}");
                }
            }

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
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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
        }).ConfigureAwait(false);
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
    /// Strips SQLCMD directives (:setvar, :on error) and client-only guards. DacFx's data-loss
    /// abort guards are kept — so the deploy stops on its own — unless the caller explicitly
    /// allows data loss, which is what the "Allow unsafe changes" checkbox means.
    /// </summary>
    public static List<string> ExtractExecutableBatches(string reorderedScript, bool allowDataLoss = false)
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
                Regex.IsMatch(cleaned, @"SET\s+NOEXEC\s+ON", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(cleaned, @"^SET\s+NOEXEC\s+OFF", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(cleaned, @"^USE\s+\[?master\]?", RegexOptions.IgnoreCase))
            {
                continue;
            }

            if (allowDataLoss)
            {
                // Skip data-loss abort guards (e.g. IF EXISTS (...) RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur...'))
                if (Regex.IsMatch(cleaned, @"The\s+schema\s+update\s+is\s+terminating\s+because\s+data\s+loss\s+might\s+occur", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(cleaned, @"Rows\s+were\s+detected\.\s*The\s+schema\s+update\s+is\s+terminating", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                // Also strip any inline data-loss check statement if mixed with other DDL in the same batch
                cleaned = Regex.Replace(cleaned, @"IF\s+EXISTS\s*\([^)]*\)\s*RAISERROR\s*\([^;]*\)(?:\s*WITH\s+NOWAIT)?;?", "", RegexOptions.IgnoreCase | RegexOptions.Singleline).Trim();
                if (string.IsNullOrWhiteSpace(cleaned)) continue;
            }

            executableBatches.Add(cleaned);
        }

        return executableBatches;
    }
}
