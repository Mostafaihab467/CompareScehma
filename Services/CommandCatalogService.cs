using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// The command palette's catalog: which objects the palette can name, and the script each
/// row writes when it is accepted. Nothing here runs anything — every script it builds is a
/// read template (SELECT / INSERT placeholder / EXEC with its real parameters) that lands in
/// the editor as text.
/// </summary>
public sealed class CommandCatalogService
{
    public sealed record CatalogObject(PaletteGroup Group, string Schema, string Name)
    {
        public string Title => $"{Schema}.{Name}";
    }

    /// <summary>
    /// User tables, views and procedures of one database. System objects are excluded because a
    /// ms-shipped row would flood the ranking of a three-letter query; the count is not capped,
    /// because a palette that quietly omits a real table is worse than a slow one.
    /// </summary>
    public async Task<List<CatalogObject>> GetObjectsAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        const string sql = """
            SELECT CASE o.type WHEN 'U' THEN 1 WHEN 'V' THEN 2 WHEN 'P' THEN 3 END,
                   s.name, o.name
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U', 'V', 'P')
              AND o.is_ms_shipped = 0
            """;

        var list = new List<CatalogObject>();
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            if (rdr.IsDBNull(0)) continue;
            var group = (PaletteGroup)rdr.GetInt32(0);
            list.Add(new CatalogObject(group, rdr.GetString(1), rdr.GetString(2)));
        }

        return list
            .OrderBy(o => (int)o.Group)
            .ThenBy(o => o.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Palette rows for the catalog. A table gets a SELECT row and, when the column cache is
    /// loaded, an INSERT row too — the cache is the one IntelliSense already filled, so the
    /// second row costs no round trip. There is deliberately no DROP row: one Enter away from
    /// a typed letter is not the place to offer a table's destruction, and the object tree's
    /// right-click and the query guard both remain.
    /// </summary>
    public IReadOnlyList<PaletteItem> RowsFor(
        IReadOnlyList<CatalogObject> objects,
        ConnectionInfo info,
        IReadOnlyDictionary<string, List<string>>? columnsByTable = null)
    {
        var rows = new List<PaletteItem>();
        foreach (var o in objects)
        {
            switch (o.Group)
            {
                case PaletteGroup.Table:
                    rows.Add(SelectRow(o));
                    if (TryColumns(columnsByTable, o, out var columns))
                        rows.Add(InsertRow(o, columns));
                    break;
                case PaletteGroup.View:
                    rows.Add(SelectRow(o));
                    break;
                case PaletteGroup.StoredProcedure:
                    rows.Add(ExecRow(o, info));
                    break;
            }
        }

        return rows;
    }

    private static PaletteItem SelectRow(CatalogObject o) => new()
    {
        Title = o.Title,
        Detail = $"{Label(o.Group)} · SELECT TOP (1000)",
        Group = o.Group,
        BuildScriptAsync = _ => Task.FromResult(ManagerScriptBuilder.SelectTop(o.Schema, o.Name))
    };

    private static PaletteItem InsertRow(CatalogObject o, IReadOnlyList<string> columns) => new()
    {
        Title = o.Title,
        Detail = "table · INSERT template",
        Group = o.Group,
        BuildScriptAsync = _ => Task.FromResult(ManagerScriptBuilder.InsertTemplate(o.Schema, o.Name, columns))
    };

    /// <summary>
    /// The EXEC row reads its parameters when accepted, not when the list is built: a catalog of
    /// procedures would otherwise mean one query per procedure for the handful the operator opens.
    /// </summary>
    private static PaletteItem ExecRow(CatalogObject o, ConnectionInfo info) => new()
    {
        Title = o.Title,
        Detail = "procedure · EXEC with its parameters",
        Group = o.Group,
        BuildScriptAsync = async ct =>
        {
            var parms = await new DbManagerService()
                .GetProcedureParamsAsync(info, o.Schema, o.Name, ct);
            return ManagerScriptBuilder.ExecTemplate(
                o.Schema, o.Name, parms.Select(p => (p.Name, p.DataType)).ToList());
        }
    };

    private static bool TryColumns(
        IReadOnlyDictionary<string, List<string>>? columnsByTable,
        CatalogObject o, out IReadOnlyList<string> columns)
    {
        columns = [];
        return columnsByTable != null &&
               columnsByTable.TryGetValue($"{o.Schema}.{o.Name}", out var found) &&
               found != null &&
               (columns = found).Count > 0;
    }

    private static string Label(PaletteGroup group) => group switch
    {
        PaletteGroup.View => "view",
        PaletteGroup.StoredProcedure => "procedure",
        _ => "table"
    };
}
