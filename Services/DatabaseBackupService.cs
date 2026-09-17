using Microsoft.SqlServer.Dac;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>Creates a portable local BACPAC from any reachable SQL Server database.</summary>
public sealed class DatabaseBackupService
{
    public Task<string> ExportBacpacAsync(ConnectionInfo database, string filePath, IProgress<string>? progress = null) => Task.Run(() =>
    {
        progress?.Report("Connecting to database for local BACPAC export...");
        var dac = new DacServices(database.ConnectionString);
        dac.Message += (_, e) => progress?.Report(e.Message.Message);

        var options = new DacExportOptions { VerifyExtraction = true };
        try
        {
            // BACPAC is written by this desktop app to the selected local path; unlike BACKUP .bak,
            // it does not require the remote SQL Server service to access the user's PC filesystem.
            dac.ExportBacpac(filePath, database.Database, options, null);
            return "Backup completed successfully.";
        }
        catch (Exception ex) when (IsUnresolvedReferenceError(ex))
        {
            progress?.Report("Schema validation failed because some objects use three-part names like [Database].[dbo].[Table] (SQL71562). Retrying without verification...");
            options.VerifyExtraction = false;
            dac.ExportBacpac(filePath, database.Database, options, null);
            return "Backup completed. Schema verification was skipped because procedures or views reference objects as [Database].[schema].[object]. Rewrite those as [schema].[object] for a fully validated package.";
        }
    });

    /// <summary>
    /// DAC export treats [ThisDatabase].[dbo].[Table] as an "external" reference and fails SQL71562
    /// even when the name is the current database.
    /// </summary>
    private static bool IsUnresolvedReferenceError(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("SQL71562", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unresolved reference", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unsupported elements were found in the schema", StringComparison.OrdinalIgnoreCase);
    }
}
