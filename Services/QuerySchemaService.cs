using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Schema metadata for IntelliSense-style completion: table/view names,
/// column names per table, and lazily-loaded on connect.
/// </summary>
public sealed class QuerySchemaService
{
    public sealed record TableInfo(string Schema, string Name)
    {
        public string FullName => $"[{Schema}].[{Name}]";
        public string ShortName => $"{Schema}.{Name}";
    }

    public async Task<List<TableInfo>> GetTablesAsync(ConnectionInfo info, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')
            ORDER BY TABLE_SCHEMA, TABLE_NAME
            """;
        var list = new List<TableInfo>();
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
            list.Add(new TableInfo(rdr.GetString(0), rdr.GetString(1)));
        return list;
    }

    public async Task<Dictionary<string, List<string>>> GetColumnsByTableAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
            """;
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var key = $"{rdr.GetString(0)}.{rdr.GetString(1)}";
            if (!map.TryGetValue(key, out var cols))
                map[key] = cols = [];
            cols.Add(rdr.GetString(2));
        }
        return map;
    }
}
