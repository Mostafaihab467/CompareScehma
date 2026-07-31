using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
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

            ct.ThrowIfCancellationRequested();
            progress?.Report("Running comparison (this may take a moment)...");
            var result = comparison.Compare();
            ct.ThrowIfCancellationRequested();

            if (result == null)
                throw new InvalidOperationException("Comparison engine returned null result.");

            var items = new List<SchemaDiffItem>();
            var processed = 0;

            // *** KEY FIX ***
            // The previous code tried to cast Differences to IList<SchemaDifference>.
            // That cast silently fails (DacFx returns a custom IEnumerable), so diffCount
            // stayed 0 and the entire foreach was skipped — causing "no differences" even
            // when real differences exist. Iterate directly instead.
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
        // Use a direct ADO.NET connection test — fast and reliable.
        // The previous DacFx self-comparison approach caused localhost to always time out
        // because DacFx builds full DACPAC snapshots just to compare an endpoint with itself.
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
            var result = comparison.Compare();
            if (result == null) throw new InvalidOperationException("Comparison returned no result.");
            var scriptResult = result.GenerateScript(targetInfo.Database);
            if (!scriptResult.Success)
                throw new InvalidOperationException(scriptResult.Message ?? scriptResult.Exception?.Message ?? "Script generation failed");
            return scriptResult.Script;
        });
    }

    public async Task<(bool Success, string Script)> ApplyChangesAsync(
        ConnectionInfo sourceInfo, ConnectionInfo targetInfo, IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            progress?.Report("Comparing schemas...");
            var comparison = new SchemaComparison(
                new SchemaCompareDatabaseEndpoint(sourceInfo.ConnectionString),
                new SchemaCompareDatabaseEndpoint(targetInfo.ConnectionString));
            var result = comparison.Compare();
            if (result == null) throw new InvalidOperationException("Comparison returned no result.");
            progress?.Report("Publishing changes to target...");
            var publishResult = result.PublishChangesToDatabase();
            if (!publishResult.Success)
            {
                var errors = publishResult.Errors?.Select(e => e.Message);
                throw new InvalidOperationException($"Deploy failed: {(errors != null ? string.Join("; ", errors) : "Unknown error")}");
            }
            progress?.Report("Generating script...");
            var scriptResult = result.GenerateScript(targetInfo.Database);
            progress?.Report("Changes applied successfully.");
            return (true, scriptResult.Success ? scriptResult.Script : string.Empty);
        });
    }
}
