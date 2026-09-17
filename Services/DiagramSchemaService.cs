using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Reads the full table/column/foreign-key schema of one SQL Server database
/// for ER-diagram rendering. Read-only: only sys.* catalog views are queried.
/// </summary>
public sealed class DiagramSchemaService
{
    public async Task<(List<DiagramTableNode> Tables, List<DiagramRelation> Relations)> GetSchemaAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        var tablesTask = ReadTablesAsync(info.ConnectionString, ct);
        var relationsTask = ReadRelationsAsync(info.ConnectionString, ct);
        var countsTask = ReadRowCountsAsync(info.ConnectionString, ct);

        await Task.WhenAll(tablesTask, relationsTask, countsTask);

        var tableMap = await tablesTask;
        var relations = await relationsTask;
        var counts = await countsTask;

        // Flag FK columns from the loaded relations.
        foreach (var rel in relations)
        {
            if (!tableMap.TryGetValue(rel.ChildTable, out var table)) continue;
            var fkCols = new HashSet<string>(rel.ChildColumns, StringComparer.OrdinalIgnoreCase);
            foreach (var col in table.Columns.Where(c => fkCols.Contains(c.Name)))
                col.IsForeignKey = true;
        }

        var tables = tableMap
            .OrderBy(kv => kv.Value.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Value.Name, StringComparer.OrdinalIgnoreCase)
            .Select(kv =>
            {
                counts.TryGetValue(kv.Key, out var rowCount);
                return new DiagramTableNode
                {
                    Schema = kv.Value.Schema,
                    Name = kv.Value.Name,
                    Columns = kv.Value.Columns,
                    RowCount = rowCount,
                };
            })
            .ToList();

        return (tables, relations);
    }

    private sealed class MutableTable
    {
        public required string Schema { get; init; }
        public required string Name { get; init; }
        public List<DiagramColumn> Columns { get; } = [];
    }

    private static async Task<Dictionary<string, MutableTable>> ReadTablesAsync(string cs, CancellationToken ct)
    {
        const string sql = """
SELECT s.name, t.name, c.name, c.column_id, c.is_identity, c.is_computed, c.is_nullable,
       ty.name, c.max_length, c.[precision], c.scale,
       CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END AS IsPk, pk.key_ordinal
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id, ic.key_ordinal
    FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    WHERE i.is_primary_key = 1
) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
WHERE t.is_ms_shipped = 0
ORDER BY s.name, t.name, c.column_id;
""";
        var map = new Dictionary<string, MutableTable>(StringComparer.OrdinalIgnoreCase);
        await using var c = new SqlConnection(cs);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 120 };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var schema = r.GetString(0);
            var table = r.GetString(1);
            var key = $"{schema}.{table}";
            if (!map.TryGetValue(key, out var t))
            {
                t = new MutableTable { Schema = schema, Name = table };
                map[key] = t;
            }
            t.Columns.Add(new DiagramColumn
            {
                Name = r.GetString(2),
                DataType = FormatType(r.GetString(7), r.GetInt16(8), r.GetByte(9), r.GetByte(10)),
                IsIdentity = r.GetBoolean(4),
                IsNullable = r.GetBoolean(6),
                IsPrimaryKey = Convert.ToInt32(r.GetValue(11)) == 1,
            });
        }
        return map;
    }

    private static string FormatType(string typeName, short maxLength, byte precision, byte scale) =>
        typeName.ToLowerInvariant() switch
        {
            "nvarchar" or "nchar" => maxLength == -1 ? $"{typeName}(max)" : $"{typeName}({maxLength / 2})",
            "varchar" or "char" or "varbinary" or "binary" => maxLength == -1 ? $"{typeName}(max)" : $"{typeName}({maxLength})",
            "decimal" or "numeric" => $"{typeName}({precision},{scale})",
            "datetime2" or "datetimeoffset" or "time" => $"{typeName}({scale})",
            _ => typeName,
        };

    private static async Task<List<DiagramRelation>> ReadRelationsAsync(string cs, CancellationToken ct)
    {
        const string sql = """
SELECT f.name, cs.name, ct.name, ps.name, pt.name, cc.name, pc.name, fkc.constraint_column_id
FROM sys.foreign_keys f
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = f.object_id
JOIN sys.tables ct ON ct.object_id = f.parent_object_id
JOIN sys.schemas cs ON cs.schema_id = ct.schema_id
JOIN sys.tables pt ON pt.object_id = f.referenced_object_id
JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
JOIN sys.columns cc ON cc.object_id = ct.object_id AND cc.column_id = fkc.parent_column_id
JOIN sys.columns pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.referenced_column_id
ORDER BY f.name, fkc.constraint_column_id;
""";
        var result = new List<DiagramRelation>();
        await using var c = new SqlConnection(cs);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 120 };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        DiagramRelation? current = null;
        var currentName = string.Empty;
        while (await r.ReadAsync(ct))
        {
            var name = r.GetString(0);
            if (current is null || !string.Equals(currentName, name, StringComparison.OrdinalIgnoreCase))
            {
                currentName = name;
                current = new DiagramRelation
                {
                    ConstraintName = name,
                    ChildTable = $"{r.GetString(1)}.{r.GetString(2)}",
                    ParentTable = $"{r.GetString(3)}.{r.GetString(4)}",
                    ChildColumns = [],
                    ParentColumns = [],
                };
                result.Add(current);
            }
            current.ChildColumns.Add(r.GetString(5));
            current.ParentColumns.Add(r.GetString(6));
        }
        return result;
    }

    private static async Task<Dictionary<string, long>> ReadRowCountsAsync(string cs, CancellationToken ct)
    {
        const string sql = """
SELECT s.name, t.name, SUM(ps.rows)
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
LEFT JOIN sys.partitions ps ON ps.object_id = t.object_id AND ps.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
GROUP BY s.name, t.name;
""";
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var c = new SqlConnection(cs);
            await c.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 120 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                result[$"{r.GetString(0)}.{r.GetString(1)}"] = r.IsDBNull(2) ? 0 : r.GetInt64(2);
        }
        catch
        {
            // Row counts are decorative — a failure must never block the diagram.
        }
        return result;
    }
}
