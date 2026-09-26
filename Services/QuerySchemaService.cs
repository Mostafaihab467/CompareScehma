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
        public string DisplayName => $"{Schema}.{Name}";
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
            var schema = rdr.GetString(0);
            var table = rdr.GetString(1);
            var col = rdr.GetString(2);

            var key = $"{schema}.{table}";
            if (!map.TryGetValue(key, out var cols))
                map[key] = cols = [];
            cols.Add(col);

            if (!map.TryGetValue(table, out var shortCols))
                map[table] = shortCols = [];
            shortCols.Add(col);
        }
        return map;
    }

    /// <summary>
    /// Every foreign key the database enforces, as one row per <em>column pair</em>: a key over
    /// two columns arrives as two rows sharing a name, which is what lets a composite join be
    /// offered whole instead of on its first column only.
    /// </summary>
    public async Task<List<ForeignKeyRef>> GetForeignKeysAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        const string sql = """
            SELECT fk.name,
                   OBJECT_SCHEMA_NAME(fk.parent_object_id),
                   OBJECT_NAME(fk.parent_object_id),
                   fc.name,
                   fkc.constraint_column_id,
                   OBJECT_SCHEMA_NAME(fk.referenced_object_id),
                   OBJECT_NAME(fk.referenced_object_id),
                   tc.name
            FROM sys.foreign_key_columns fkc
            JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
            JOIN sys.columns fc ON fc.object_id = fkc.parent_object_id
                               AND fc.column_id = fkc.parent_column_id
            JOIN sys.columns tc ON tc.object_id = fkc.referenced_object_id
                               AND tc.column_id = fkc.referenced_column_id
            ORDER BY fk.name, fkc.constraint_column_id
            """;
        var list = new List<ForeignKeyRef>();
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            // A key aimed at something the metadata no longer names (a dropped view, an object in
            // another database) has no schema to report, and a null would become the string "NULL"
            // in the middle of a join clause.
            if (rdr.IsDBNull(1) || rdr.IsDBNull(2) || rdr.IsDBNull(5) || rdr.IsDBNull(6))
                continue;
            list.Add(new ForeignKeyRef(
                rdr.GetString(0), rdr.GetString(1), rdr.GetString(2), rdr.GetString(3),
                rdr.GetString(5), rdr.GetString(6), rdr.GetString(7), rdr.GetInt32(4)));
        }
        return list;
    }
}

