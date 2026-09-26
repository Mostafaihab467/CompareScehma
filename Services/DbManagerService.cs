using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Data-access layer for the DB Manager window.
/// All methods open their own connection and are safe to call concurrently.
/// </summary>
public class DbManagerService
{
    // -------------------------------------------------------------------------
    // Object browsing
    // -------------------------------------------------------------------------

    /// <summary>Returns all objects of the requested type from the database.
    /// When <paramref name="filter"/> carries name/schema patterns they are
    /// applied server-side via escaped LIKE predicates.</summary>
    public async Task<List<DbObjectInfo>> GetObjectsAsync(
        ConnectionInfo info, DbObjectType objectType, CancellationToken ct = default,
        ExplorerQueryFilter? filter = null)
    {
        var results = new List<DbObjectInfo>();

        if (filter != null && !TypeMatchesFilter(objectType, filter))
            return results;

        var (typeFilter, query) = objectType switch
        {
            DbObjectType.Table => ("U",
                """
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_SCHEMA, TABLE_NAME
                """),
            DbObjectType.View => ("V",
                """
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.VIEWS
                ORDER BY TABLE_SCHEMA, TABLE_NAME
                """),
            DbObjectType.StoredProcedure => ("P",
                """
                SELECT SPECIFIC_SCHEMA, SPECIFIC_NAME
                FROM INFORMATION_SCHEMA.ROUTINES
                WHERE ROUTINE_TYPE = 'PROCEDURE'
                ORDER BY SPECIFIC_SCHEMA, SPECIFIC_NAME
                """),
            DbObjectType.Function => ("FN",
                """
                SELECT SPECIFIC_SCHEMA, SPECIFIC_NAME
                FROM INFORMATION_SCHEMA.ROUTINES
                WHERE ROUTINE_TYPE = 'FUNCTION'
                ORDER BY SPECIFIC_SCHEMA, SPECIFIC_NAME
                """),
            DbObjectType.Trigger => ("TR",
                """
                SELECT s.name AS SCHEMA_NAME, t.name AS TRIGGER_NAME
                FROM sys.triggers t
                JOIN sys.objects o ON t.parent_id = o.object_id
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                ORDER BY s.name, t.name
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(objectType))
        };
        _ = typeFilter; // suppress unused warning

        var namePattern = LikePattern(filter?.NamePattern);
        var schemaPattern = LikePattern(filter?.SchemaPattern);
        if (namePattern != null || schemaPattern != null)
            query = AddLikePredicate(query, objectType, namePattern != null, schemaPattern != null);

        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn);
        if (namePattern != null) cmd.Parameters.Add("@objName", SqlDbType.NVarChar, 260).Value = namePattern;
        if (schemaPattern != null) cmd.Parameters.Add("@objSchema", SqlDbType.NVarChar, 128).Value = schemaPattern;
        cmd.CommandTimeout = 30;
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            results.Add(new DbObjectInfo
            {
                Schema = rdr.GetString(0),
                Name = rdr.GetString(1),
                ObjectType = objectType
            });
        }

        return results;
    }

    /// <summary>Searches across all object types by name fragment.</summary>
    public async Task<List<DbObjectInfo>> SearchObjectsAsync(
        ConnectionInfo info, string searchTerm, CancellationToken ct = default)
    {
        const string query = """
            SELECT s.name AS schema_name, o.name AS obj_name,
                   o.type_desc
            FROM sys.objects o
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE o.name LIKE @term
              AND o.type IN ('U','V','P','FN','IF','TF','TR')
            ORDER BY o.type, s.name, o.name
            """;

        var results = new List<DbObjectInfo>();
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@term", $"%{searchTerm}%");
        cmd.CommandTimeout = 30;

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var typeDesc = rdr.GetString(2);
            var objType = typeDesc switch
            {
                "USER_TABLE"            => DbObjectType.Table,
                "VIEW"                  => DbObjectType.View,
                "SQL_STORED_PROCEDURE"  => DbObjectType.StoredProcedure,
                "SQL_SCALAR_FUNCTION" or
                "SQL_INLINE_TABLE_VALUED_FUNCTION" or
                "SQL_TABLE_VALUED_FUNCTION" => DbObjectType.Function,
                "SQL_TRIGGER"           => DbObjectType.Trigger,
                _                       => DbObjectType.Table
            };
            results.Add(new DbObjectInfo
            {
                Schema = rdr.GetString(0),
                Name = rdr.GetString(1),
                ObjectType = objType
            });
        }
        return results;
    }

    // -------------------------------------------------------------------------
    // Table structure
    // -------------------------------------------------------------------------

    public async Task<List<TableColumn>> GetTableColumnsAsync(
        ConnectionInfo info, string schema, string tableName, CancellationToken ct = default)
    {
        const string query = """
            SELECT
                c.ORDINAL_POSITION,
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE,
                c.IS_NULLABLE,
                COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IS_IDENTITY,
                c.COLUMN_DEFAULT,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PK
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                  AND ku.TABLE_SCHEMA = @schema
                  AND ku.TABLE_NAME   = @table
            ) pk ON pk.COLUMN_NAME = c.COLUMN_NAME
            WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
            ORDER BY c.ORDINAL_POSITION
            """;

        var cols = new List<TableColumn>();
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.CommandTimeout = 30;

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            cols.Add(new TableColumn
            {
                OrdinalPosition  = rdr.GetInt32(0),
                Name             = rdr.GetString(1),
                DataType         = rdr.GetString(2),
                MaxLength        = rdr.IsDBNull(3) ? null : rdr.GetInt32(3),
                NumericPrecision = rdr.IsDBNull(4) ? null : (int?)rdr.GetByte(4),
                NumericScale     = rdr.IsDBNull(5) ? null : rdr.GetInt32(5),
                IsNullable       = rdr.GetString(6) == "YES",
                IsIdentity       = rdr.IsDBNull(7) ? false : rdr.GetInt32(7) == 1,
                DefaultValue     = rdr.IsDBNull(8) ? null : rdr.GetString(8),
                IsPrimaryKey     = rdr.GetInt32(9) == 1
            });
        }
        return cols;
    }

    // -------------------------------------------------------------------------
    // Table data
    // -------------------------------------------------------------------------

    public async Task<(List<string> Columns, List<Dictionary<string, object?>> Rows)> GetTableDataAsync(
        ConnectionInfo info, string schema, string tableName,
        IEnumerable<TableFilter>? filters = null,
        int topN = 500,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var topClause = topN > 0 ? $"TOP ({topN}) " : "";
        var safeSchema = schema.Replace("]", "]]");
        var safeTable = tableName.Replace("]", "]]");
        sb.Append($"SELECT {topClause}* FROM [{safeSchema}].[{safeTable}]");

        var validFilters = (filters ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.ColumnName))
            .ToList();
        var sqlParams = new List<SqlParameter>();

        if (validFilters.Count > 0)
        {
            var whereClauses = new List<string>();
            int paramIndex = 0;

            foreach (var f in validFilters)
            {
                var cleanCol = f.ColumnName.Trim().TrimStart('[').TrimEnd(']');
                if (string.IsNullOrWhiteSpace(cleanCol)) continue;

                var col = $"[{cleanCol.Replace("]", "]]")}]";
                var op = (f.Operator ?? "=").Trim().ToUpperInvariant();
                var prefix = whereClauses.Count > 0
                    ? (string.Equals(f.LogicalOp, "OR", StringComparison.OrdinalIgnoreCase) ? "OR " : "AND ")
                    : "";

                switch (op)
                {
                    case "IS NULL":
                        whereClauses.Add($"{prefix}{col} IS NULL");
                        break;

                    case "IS NOT NULL":
                        whereClauses.Add($"{prefix}{col} IS NOT NULL");
                        break;

                    case "IN":
                    case "NOT IN":
                        var inVals = (f.Value ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (inVals.Length == 0) continue;

                        var inParamNames = new List<string>();
                        for (int vi = 0; vi < inVals.Length; vi++)
                        {
                            var pName = $"@p{paramIndex}_{vi}";
                            inParamNames.Add(pName);
                            sqlParams.Add(new SqlParameter(pName, inVals[vi]));
                        }
                        whereClauses.Add($"{prefix}{col} {op} ({string.Join(", ", inParamNames)})");
                        paramIndex++;
                        break;

                    case "BETWEEN":
                        var parts = (f.Value ?? "").Split(',', 2);
                        if (parts.Length < 2) continue;
                        var p1 = $"@p{paramIndex}a";
                        var p2 = $"@p{paramIndex}b";
                        sqlParams.Add(new SqlParameter(p1, parts[0].Trim()));
                        sqlParams.Add(new SqlParameter(p2, parts[1].Trim()));
                        whereClauses.Add($"{prefix}{col} BETWEEN {p1} AND {p2}");
                        paramIndex++;
                        break;

                    case "LIKE":
                    case "NOT LIKE":
                        var likeParam = $"@p{paramIndex++}";
                        whereClauses.Add($"{prefix}{col} {op} {likeParam}");
                        sqlParams.Add(new SqlParameter(likeParam, f.Value ?? string.Empty));
                        break;

                    case "=":
                    case "!=":
                    case "<>":
                    case ">":
                    case "<":
                    case ">=":
                    case "<=":
                        var cmpParam = $"@p{paramIndex++}";
                        whereClauses.Add($"{prefix}{col} {op} {cmpParam}");
                        sqlParams.Add(new SqlParameter(cmpParam, (object?)f.Value ?? DBNull.Value));
                        break;

                    default:
                        var defParam = $"@p{paramIndex++}";
                        whereClauses.Add($"{prefix}{col} = {defParam}");
                        sqlParams.Add(new SqlParameter(defParam, (object?)f.Value ?? DBNull.Value));
                        break;
                }
            }

            if (whereClauses.Count > 0)
            {
                sb.Append(" WHERE ");
                sb.Append(string.Join(" ", whereClauses));
            }
        }

        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sb.ToString(), conn);
        cmd.Parameters.AddRange([.. sqlParams]);
        cmd.CommandTimeout = 60;

        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        var columns = new List<string>();
        for (int i = 0; i < rdr.FieldCount; i++)
            columns.Add(rdr.GetName(i));

        var rows = new List<Dictionary<string, object?>>();
        while (await rdr.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < rdr.FieldCount; i++)
                row[columns[i]] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
            rows.Add(row);
        }
        return (columns, rows);
    }

    // -------------------------------------------------------------------------
    // Table metadata (SSMS-style explorer folders)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads keys, indexes, triggers, statistics and partition info for a table in
    /// one round trip series on a single connection. Column list comes from the
    /// existing <see cref="GetTableColumnsAsync"/>.
    /// </summary>
    public async Task<TableMetadata> GetTableMetadataAsync(
        ConnectionInfo info, string schema, string tableName, CancellationToken ct = default)
    {
        var meta = new TableMetadata { Schema = schema, Name = tableName };
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);

        // Columns (reuse)
        meta.Columns = await GetTableColumnsAsync(info, schema, tableName, ct);

        // PK / unique keys
        var keys = new Dictionary<string, MetaKey>(StringComparer.OrdinalIgnoreCase);
        const string keyQuery = """
            SELECT kc.name, kc.type_desc, c.name AS col
            FROM sys.key_constraints kc
            JOIN sys.tables t ON kc.parent_object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY kc.name, ic.key_ordinal
            """;
        await using (var cmd = new SqlCommand(keyQuery, conn))
        {
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", tableName);
            cmd.CommandTimeout = 30;
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                var name = rdr.GetString(0);
                var kind = rdr.GetString(1) == "PRIMARY_KEY_CONSTRAINT" ? "PK" : "UQ";
                if (!keys.TryGetValue(name, out var k))
                    keys[name] = k = new MetaKey(name, kind, [], null);
                k.Columns.Add(rdr.GetString(2));
            }
        }

        // Foreign keys
        const string fkQuery = """
            SELECT fk.name, c.name AS col, rt.name AS ref_table
            FROM sys.foreign_keys fk
            JOIN sys.tables t ON fk.parent_object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
            JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY fk.name, fkc.constraint_column_id
            """;
        await using (var cmd = new SqlCommand(fkQuery, conn))
        {
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", tableName);
            cmd.CommandTimeout = 30;
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                var name = rdr.GetString(0);
                if (!keys.TryGetValue(name, out var k))
                    keys[name] = k = new MetaKey(name, "FK", [], rdr.GetString(2));
                k.Columns.Add(rdr.GetString(1));
            }
        }
        meta.Keys = keys.Values.ToList();

        // Indexes (key + included columns grouped client-side)
        const string idxQuery = """
            SELECT i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint,
                   i.is_disabled, i.has_filter, CAST(i.filter_definition AS NVARCHAR(4000)),
                   c.name AS col, ic.is_included_column, ic.key_ordinal
            FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE s.name = @schema AND t.name = @table AND i.name IS NOT NULL
            ORDER BY i.name, ic.is_included_column, ic.key_ordinal
            """;
        var idx = new Dictionary<string, MetaIndex>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand(idxQuery, conn))
        {
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", tableName);
            cmd.CommandTimeout = 30;
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                var name = rdr.GetString(0);
                if (!idx.TryGetValue(name, out var ix))
                    idx[name] = ix = new MetaIndex(
                        name, rdr.GetString(1), rdr.GetBoolean(2), rdr.GetBoolean(3),
                        rdr.GetBoolean(4), rdr.GetBoolean(5),
                        rdr.GetBoolean(6) ? rdr.IsDBNull(7) ? null : rdr.GetString(7) : null,
                        [], []);
                if (rdr.GetBoolean(9)) ix.IncludedColumns.Add(rdr.GetString(8));
                else ix.KeyColumns.Add(rdr.GetString(8));
            }
        }
        meta.Indexes = idx.Values.ToList();

        // Triggers on this table
        const string trgQuery = """
            SELECT t.name
            FROM sys.triggers t
            JOIN sys.tables tb ON t.parent_id = tb.object_id
            JOIN sys.schemas s ON tb.schema_id = s.schema_id
            WHERE s.name = @schema AND tb.name = @table
            ORDER BY t.name
            """;
        var trg = new List<string>();
        await using (var cmd = new SqlCommand(trgQuery, conn))
        {
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", tableName);
            cmd.CommandTimeout = 30;
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                trg.Add(rdr.GetString(0));
        }
        meta.Triggers = trg;

        // Statistics (with live row counts from dm_db_stats_properties when available)
        const string statQuery = """
            SELECT s.name, COALESCE(spp.rows, 0), spp.last_updated
            FROM sys.stats s
            JOIN sys.tables t ON s.object_id = t.object_id
            JOIN sys.schemas sch ON t.schema_id = sch.schema_id
            OUTER APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) spp
            WHERE sch.name = @schema AND t.name = @table
            ORDER BY s.name
            """;
        var stats = new List<MetaStat>();
        await using (var cmd = new SqlCommand(statQuery, conn))
        {
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", tableName);
            cmd.CommandTimeout = 30;
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                stats.Add(new MetaStat(
                    rdr.GetString(0),
                    Convert.ToInt64(rdr.GetValue(1)),
                    rdr.IsDBNull(2) ? null : (DateTime)rdr.GetValue(2)));
        }
        meta.Stats = stats;

        // Partitions (heap or clustered; boundary + scheme when partitioned)
        const string partQuery = """
            SELECT p.partition_number, p.rows,
                   pf.name, ps.name,
                   CAST(rv.value AS NVARCHAR(4000)),
                   COALESCE(fg.name, fg0.name)
            FROM sys.partitions p
            JOIN sys.tables t ON p.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            LEFT JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id
            LEFT JOIN sys.partition_schemes ps ON i.data_space_id = ps.data_space_id
            LEFT JOIN sys.partition_functions pf ON ps.function_id = pf.function_id
            LEFT JOIN sys.partition_range_values rv ON rv.function_id = pf.function_id AND rv.boundary_id = p.partition_number
            LEFT JOIN sys.destination_data_spaces dds ON dds.partition_scheme_id = ps.data_space_id AND dds.destination_id = p.partition_number
            LEFT JOIN sys.filegroups fg ON fg.data_space_id = dds.data_space_id
            LEFT JOIN sys.filegroups fg0 ON fg0.data_space_id = i.data_space_id
            WHERE s.name = @schema AND t.name = @table AND p.index_id IN (0, 1)
            ORDER BY p.partition_number
            """;
        var parts = new List<MetaPartition>();
        await using (var cmd = new SqlCommand(partQuery, conn))
        {
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", tableName);
            cmd.CommandTimeout = 30;
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                parts.Add(new MetaPartition(
                    rdr.GetInt32(0),
                    rdr.GetInt64(1),
                    rdr.IsDBNull(2) ? null : rdr.GetString(2),
                    rdr.IsDBNull(3) ? null : rdr.GetString(3),
                    rdr.IsDBNull(4) ? null : rdr.GetString(4),
                    rdr.IsDBNull(5) ? null : rdr.GetString(5)));
        }
        meta.Partitions = parts;

        return meta;
    }

    // -------------------------------------------------------------------------
    // Object definitions
    // -------------------------------------------------------------------------

    public async Task<string> GetObjectDefinitionAsync(
        ConnectionInfo info, string schema, string objectName,
        DbObjectType type, CancellationToken ct = default)
    {
        // For tables, generate a CREATE TABLE script manually
        if (type == DbObjectType.Table)
            return await ScriptTableDefinitionAsync(info, schema, objectName, ct);

        const string query = "SELECT OBJECT_DEFINITION(OBJECT_ID(@fullName))";
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@fullName", $"{schema}.{objectName}");
        cmd.CommandTimeout = 30;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is DBNull or null ? $"-- Definition not available for [{schema}].[{objectName}]" : (string)result;
    }

    /// <summary>
    /// Executes a T-SQL batch or definition (e.g. ALTER/CREATE PROCEDURE, VIEW, FUNCTION).
    /// </summary>
    public async Task ExecuteRawScriptAsync(ConnectionInfo info, string script, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(script))
            throw new ArgumentException("Script content cannot be empty.", nameof(script));

        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);

        // Split by GO if user edited multiple batches
        var batches = SchemaCompareService.ExtractExecutableBatches(script);
        if (batches.Count == 0)
            batches.Add(script.Trim());

        foreach (var batch in batches)
        {
            await using var cmd = new SqlCommand(batch, conn);
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<string> ScriptTableDefinitionAsync(
        ConnectionInfo info, string schema, string tableName, CancellationToken ct)
    {
        var cols = await GetTableColumnsAsync(info, schema, tableName, ct);
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE [{schema}].[{tableName}] (");

        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            var comma = i < cols.Count - 1 ? "," : "";
            var identity = c.IsIdentity ? " IDENTITY(1,1)" : "";
            var nullable = c.IsNullable ? " NULL" : " NOT NULL";
            var def = c.DefaultValue != null ? $" DEFAULT {c.DefaultValue}" : "";
            sb.AppendLine($"    [{c.Name}] {c.DisplayType}{identity}{nullable}{def}{comma}");
        }

        var pks = cols.Where(c => c.IsPrimaryKey).Select(c => $"[{c.Name}]").ToList();
        if (pks.Count > 0)
        {
            sb.AppendLine($"    CONSTRAINT [PK_{tableName}] PRIMARY KEY ({string.Join(", ", pks)})");
        }

        sb.AppendLine(");");
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Stored procedure parameters
    // -------------------------------------------------------------------------

    public async Task<List<StoredProcParam>> GetProcedureParamsAsync(
        ConnectionInfo info, string schema, string procName, CancellationToken ct = default)
    {
        // The type carries its length or precision: "nvarchar" tells whoever fills the template
        // nothing about how long the value may be, and a truncating EXEC is their problem.
        const string query = """
            SELECT
                p.name,
                ty.name + CASE
                    WHEN ty.name IN ('nvarchar', 'nchar') AND p.max_length < 0 THEN '(max)'
                    WHEN ty.name IN ('nvarchar', 'nchar') THEN '(' + CAST(p.max_length / 2 AS varchar(10)) + ')'
                    WHEN ty.name IN ('varchar', 'char', 'varbinary') AND p.max_length < 0 THEN '(max)'
                    WHEN ty.name IN ('varchar', 'char', 'varbinary') THEN '(' + CAST(p.max_length AS varchar(10)) + ')'
                    WHEN ty.name IN ('decimal', 'numeric') THEN '(' + CAST(p.precision AS varchar(10)) + ',' + CAST(p.scale AS varchar(10)) + ')'
                    ELSE '' END,
                p.is_output,
                p.has_default_value,
                CAST(p.default_value AS NVARCHAR(256))
            FROM sys.parameters p
            JOIN sys.objects o ON p.object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            JOIN sys.types ty ON ty.user_type_id = p.user_type_id
            WHERE s.name = @schema AND o.name = @proc
              AND p.parameter_id > 0
            ORDER BY p.parameter_id
            """;

        var parms = new List<StoredProcParam>();
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@proc", procName);
        cmd.CommandTimeout = 30;

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            parms.Add(new StoredProcParam
            {
                Name         = rdr.GetString(0),
                DataType     = rdr.GetString(1),
                IsOutput     = rdr.GetBoolean(2),
                DefaultValue = rdr.IsDBNull(4) ? null : rdr.GetString(4)
            });
        }
        return parms;
    }

    // -------------------------------------------------------------------------
    // Execute stored procedure
    // -------------------------------------------------------------------------

    public async Task<(List<string> Columns, List<Dictionary<string, object?>> Rows, Dictionary<string, object?> OutputParams, string Messages)>
        ExecuteProcedureAsync(
            ConnectionInfo info, string schema, string procName,
            IEnumerable<StoredProcParam> parameters,
            CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(info.ConnectionString);
        var messages = new StringBuilder();
        conn.InfoMessage += (_, e) => messages.AppendLine(e.Message);

        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand($"[{schema}].[{procName}]", conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };

        var sqlParams = new Dictionary<string, SqlParameter>();
        foreach (var p in parameters)
        {
            var sp = new SqlParameter(p.Name, string.IsNullOrWhiteSpace(p.InputValue) ? DBNull.Value : p.InputValue)
            {
                Direction = p.IsOutput
                    ? ParameterDirection.InputOutput
                    : ParameterDirection.Input
            };
            cmd.Parameters.Add(sp);
            sqlParams[p.Name] = sp;
        }

        var columns = new List<string>();
        var rows = new List<Dictionary<string, object?>>();

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        for (int i = 0; i < rdr.FieldCount; i++)
            columns.Add(rdr.GetName(i));
        while (await rdr.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < rdr.FieldCount; i++)
                row[columns[i]] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
            rows.Add(row);
        }

        var outputVals = new Dictionary<string, object?>();
        foreach (var kv in sqlParams.Where(x => x.Value.Direction != ParameterDirection.Input))
            outputVals[kv.Key] = kv.Value.Value is DBNull ? null : kv.Value.Value;

        return (columns, rows, outputVals, messages.ToString());
    }

    // -------------------------------------------------------------------------
    // Row editing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Updates a single row. pkValues identifies the row; changes is the new column→value map.
    /// </summary>
    public async Task UpdateRowAsync(
        ConnectionInfo info, string schema, string tableName,
        Dictionary<string, object?> pkValues,
        Dictionary<string, object?> changes,
        CancellationToken ct = default)
    {
        var validChanges = changes.Where(k => !string.IsNullOrWhiteSpace(k.Key)).ToList();
        var validPk = pkValues.Where(k => !string.IsNullOrWhiteSpace(k.Key)).ToList();
        if (validChanges.Count == 0 || validPk.Count == 0) return;

        var setClause = string.Join(", ", validChanges.Select((kv, i) => $"[{kv.Key.Replace("]", "]]")}] = @set{i}"));
        var whereClause = string.Join(" AND ", validPk.Select((kv, i) => $"[{kv.Key.Replace("]", "]]")}] = @pk{i}"));
        var sql = $"UPDATE [{schema.Replace("]", "]]")}].[{tableName.Replace("]", "]]")}] SET {setClause} WHERE {whereClause}";

        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 30;

        for (int i = 0; i < validChanges.Count; i++)
            cmd.Parameters.AddWithValue($"@set{i}", validChanges[i].Value ?? DBNull.Value);
        for (int j = 0; j < validPk.Count; j++)
            cmd.Parameters.AddWithValue($"@pk{j}", validPk[j].Value ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Inserts a new row.</summary>
    public async Task InsertRowAsync(
        ConnectionInfo info, string schema, string tableName,
        Dictionary<string, object?> values,
        CancellationToken ct = default)
    {
        var validValues = values.Where(k => !string.IsNullOrWhiteSpace(k.Key)).ToList();
        if (validValues.Count == 0) return;

        var cols = string.Join(", ", validValues.Select(kv => $"[{kv.Key.Replace("]", "]]")}]"));
        var parms = string.Join(", ", validValues.Select((_, i) => $"@v{i}"));
        var sql = $"INSERT INTO [{schema.Replace("]", "]]")}].[{tableName.Replace("]", "]]")}] ({cols}) VALUES ({parms})";

        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 30;
        for (int i = 0; i < validValues.Count; i++)
            cmd.Parameters.AddWithValue($"@v{i}", validValues[i].Value ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Deletes a row identified by pkValues.</summary>
    public async Task DeleteRowAsync(
        ConnectionInfo info, string schema, string tableName,
        Dictionary<string, object?> pkValues,
        CancellationToken ct = default)
    {
        var validPk = pkValues.Where(k => !string.IsNullOrWhiteSpace(k.Key)).ToList();
        if (validPk.Count == 0) return;

        var whereClause = string.Join(" AND ", validPk.Select((kv, i) => $"[{kv.Key.Replace("]", "]]")}] = @pk{i}"));
        var sql = $"DELETE FROM [{schema.Replace("]", "]]")}].[{tableName.Replace("]", "]]")}] WHERE {whereClause}";

        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 30;
        for (int j = 0; j < validPk.Count; j++)
            cmd.Parameters.AddWithValue($"@pk{j}", validPk[j].Value ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // -------------------------------------------------------------------------
    // Database properties / security / restore
    // -------------------------------------------------------------------------

    /// <summary>Core properties + file list of the connected database (SSMS
    /// Properties dialog). FILEPROPERTY only works inside the file's own
    /// database — the connection already points there.</summary>
    public async Task<DatabaseProperties> GetDatabasePropertiesAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);

        var files = new List<DbFileInfo>();
        string name = "", compat = "", collation = "", recovery = "", state = "",
               userAccess = "", logReuse = "";
        DateTime? created = null;
        var rcsi = false;
        double logTotalMb = 0, logUsedMb = 0;

        await using (var cmd = new SqlCommand("""
            SELECT d.name, d.create_date, d.compatibility_level, d.collation_name,
                   d.recovery_model_desc, d.state_desc, d.user_access_desc, d.log_reuse_wait_desc,
                   d.is_read_committed_snapshot_on,
                   mf.name, mf.type_desc, mf.state_desc,
                   mf.size * 8 / 1024.0,
                   CAST(FILEPROPERTY(mf.name, 'SpaceUsed') AS bigint) * 8 / 1024.0,
                   mf.physical_name, mf.growth, mf.is_percent_growth
            FROM sys.databases AS d
            CROSS JOIN sys.database_files AS mf
            WHERE d.database_id = DB_ID()
            ORDER BY mf.file_id;

            SELECT total_log_size_in_bytes / 1048576.0, used_log_space_in_bytes / 1048576.0
            FROM sys.dm_db_log_space_usage;
            """, conn) { CommandTimeout = 30 })
        {
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                name = rdr.GetString(0);
                created = rdr.GetDateTime(1);
                compat = Convert.ToInt32(rdr.GetValue(2), System.Globalization.CultureInfo.InvariantCulture).ToString();
                collation = rdr.GetString(3);
                recovery = rdr.GetString(4);
                state = rdr.GetString(5);
                userAccess = rdr.GetString(6);
                logReuse = rdr.GetString(7);
                rcsi = rdr.GetBoolean(8);

                var growth = rdr.GetInt32(15);
                var growthText = rdr.GetBoolean(16)
                    ? $"{growth} %"
                    : $"{growth * 8 / 1024.0:N0} MB";
                files.Add(new DbFileInfo
                {
                    Name = rdr.GetString(9),
                    TypeDesc = rdr.GetString(10),
                    StateDesc = rdr.GetString(11),
                    SizeMb = Convert.ToDouble(rdr.GetValue(12), System.Globalization.CultureInfo.InvariantCulture),
                    UsedMb = Convert.ToDouble(rdr.GetValue(13), System.Globalization.CultureInfo.InvariantCulture),
                    PhysicalName = rdr.GetString(14),
                    GrowthText = growthText
                });
            }
            if (await rdr.NextResultAsync(ct) && await rdr.ReadAsync(ct))
            {
                logTotalMb = Convert.ToDouble(rdr.GetValue(0), System.Globalization.CultureInfo.InvariantCulture);
                logUsedMb = Convert.ToDouble(rdr.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (name.Length == 0)
            throw new InvalidOperationException("Could not read database properties.");
        return new DatabaseProperties
        {
            Name = name,
            CreatedUtc = DateTime.SpecifyKind(created!.Value, DateTimeKind.Unspecified).ToUniversalTime(),
            CompatibilityLevel = compat,
            Collation = collation,
            RecoveryModel = recovery,
            State = state,
            UserAccess = userAccess,
            LogReuseWait = logReuse,
            IsRcsi = rcsi,
            Files = files,
            LogTotalMb = logTotalMb,
            LogUsedMb = logUsedMb
        };
    }

    /// <summary>Database users and roles for the read-only Security folder.</summary>
    public async Task<(List<DbPrincipalRow> Users, List<DbPrincipalRow> Roles)> GetDbSecurityAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        const string query = """
            SELECT dp.name, dp.type_desc,
                   ISNULL(dp.default_schema_name, N'') AS default_schema,
                   dp.create_date, dp.is_fixed_role,
                   ISNULL(o.name, N'') AS owner_name
            FROM sys.database_principals AS dp
            LEFT JOIN sys.database_principals AS o ON o.principal_id = dp.owning_principal_id
            WHERE dp.type IN (N'S', N'U', N'G', N'R')
              AND dp.name NOT IN (N'INFORMATION_SCHEMA', N'sys', N'guest')
            """;

        var users = new List<DbPrincipalRow>();
        var roles = new List<DbPrincipalRow>();
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var row = new DbPrincipalRow
            {
                Name = rdr.GetString(0),
                TypeDesc = rdr.GetString(1),
                DefaultSchema = rdr.GetString(2),
                CreatedUtc = rdr.GetDateTime(3),
                IsFixedRole = rdr.GetBoolean(4),
                Owner = rdr.GetString(5)
            };
            if (row.TypeDesc.Contains("ROLE", StringComparison.OrdinalIgnoreCase))
                roles.Add(row);
            else
                users.Add(row);
        }
        users.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        // Fixed roles (db_owner…) first, like SSMS; custom roles after.
        roles.Sort((a, b) =>
        {
            var byFixed = a.IsFixedRole == b.IsFixedRole
                ? string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                : a.IsFixedRole ? -1 : 1;
            return byFixed;
        });
        return (users, roles);
    }

    // -------------------------------------------------------------------------
    // Server-level browsing (the tree above the connected database)
    // Every query here runs against master, so a database-scoped ConnectionInfo
    // is retargeted by MasterConnectionFor before it is opened.
    // -------------------------------------------------------------------------

    /// <summary>Version / edition / collation for the server node's label.</summary>
    public async Task<ServerOverview> GetServerOverviewAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        const string query = """
            SELECT ISNULL(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)), N''),
                   ISNULL(CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)), N''),
                   ISNULL(CAST(SERVERPROPERTY('Edition') AS nvarchar(128)), N''),
                   ISNULL(CAST(SERVERPROPERTY('Collation') AS nvarchar(128)), N'')
            """;

        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 15 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct))
            return new ServerOverview(info.Server, "", "", "");
        return new ServerOverview(
            rdr.IsDBNull(0) ? info.Server : rdr.GetString(0),
            rdr.GetString(1), rdr.GetString(2), rdr.GetString(3));
    }

    /// <summary>Every database on the instance, name-ordered. The explorer marks the
    /// connected one separately, so the caller can match on <see cref="ServerDatabaseRow.Name"/>.</summary>
    public async Task<List<ServerDatabaseRow>> GetDatabasesAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        const string query = """
            SELECT d.name, d.state_desc,
                   CAST(ISNULL((SELECT SUM(mf.size) * 8 / 1024
                                FROM sys.master_files AS mf
                                WHERE mf.database_id = d.database_id), 0) AS bigint) AS size_mb
            FROM sys.databases AS d
            ORDER BY d.name
            """;

        var rows = new List<ServerDatabaseRow>();
        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
            rows.Add(new ServerDatabaseRow(rdr.GetString(0), rdr.GetString(1), Convert.ToInt64(rdr.GetValue(2))));
        return rows;
    }

    /// <summary>Server logins and server roles for the read-only Security folder.</summary>
    public async Task<(List<ServerPrincipalRow> Logins, List<ServerPrincipalRow> Roles)> GetServerSecurityAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        const string query = """
            SELECT name, type_desc, ISNULL(default_database_name, N''), is_disabled
            FROM sys.server_principals
            WHERE type IN (N'S', N'U', N'G', N'R')
            ORDER BY CASE WHEN type = N'R' THEN 1 ELSE 0 END, name
            """;

        var logins = new List<ServerPrincipalRow>();
        var roles = new List<ServerPrincipalRow>();
        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var row = new ServerPrincipalRow(rdr.GetString(0), rdr.GetString(1), rdr.GetString(2),
                !rdr.IsDBNull(3) && rdr.GetBoolean(3));
            if (row.TypeDesc.Contains("ROLE", StringComparison.OrdinalIgnoreCase)) roles.Add(row);
            else logins.Add(row);
        }
        return (logins, roles);
    }

    /// <summary>Linked servers, including the implicit <c>(local)</c> entry.</summary>
    public async Task<List<LinkedServerRow>> GetLinkedServersAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        const string query = """
            SELECT s.server_id, s.name, ISNULL(s.product, N''), ISNULL(s.data_source, N'')
            FROM sys.servers AS s
            ORDER BY s.server_id
            """;

        var rows = new List<LinkedServerRow>();
        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
            rows.Add(new LinkedServerRow(rdr.GetString(1), rdr.GetString(2), rdr.GetString(3),
                Convert.ToInt32(rdr.GetValue(0))));
        return rows;
    }

    /// <summary>SQL Agent jobs with their last-run outcome. Returns null when the
    /// instance has no msdb — Express Edition and Azure SQL both qualify, and a
    /// missing Agent is a fact to display, not an error to throw.</summary>
    public async Task<List<AgentJobRow>?> GetAgentJobsAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);

        await using var probe = new SqlCommand("SELECT DB_ID(N'msdb')", conn) { CommandTimeout = 15 };
        if (await probe.ExecuteScalarAsync(ct) is DBNull or null)
            return null;

        const string query = """
            SELECT j.name,
                   CONVERT(bit, j.enabled),
                   ISNULL(c.name, N''),
                   h.run_status,
                   h.run_date,
                   h.run_time
            FROM msdb.dbo.sysjobs AS j
            LEFT JOIN msdb.dbo.syscategories AS c
                   ON c.category_id = j.category_id AND c.category_class = 1
            OUTER APPLY (SELECT TOP 1 rh.run_status, rh.run_date, rh.run_time
                         FROM msdb.dbo.sysjobhistory AS rh
                         WHERE rh.job_id = j.job_id AND rh.step_id = 0
                         ORDER BY rh.run_date DESC, rh.run_time DESC) AS h
            ORDER BY j.name
            """;

        var rows = new List<AgentJobRow>();
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var enabled = !rdr.IsDBNull(1) && rdr.GetBoolean(1);
            var hasHistory = !rdr.IsDBNull(3);
            var outcome = !hasHistory ? "No history" : Convert.ToInt32(rdr.GetValue(3)) switch
            {
                0 => "Failed",
                1 => "Succeeded",
                2 => "Retried",
                3 => "Cancelled",
                4 => "In progress",
                _ => "Unknown"
            };
            DateTime? when = null;
            if (hasHistory && !rdr.IsDBNull(4) && rdr.GetInt32(4) > 0)
            {
                var d = rdr.GetInt32(4);
                var t = rdr.IsDBNull(5) ? 0 : rdr.GetInt32(5);
                try
                {
                    when = new DateTime(d / 10000, d / 100 % 100, d % 100,
                        t / 10000, t / 100 % 100, t % 100);
                }
                catch (ArgumentOutOfRangeException)
                {
                    when = null; // a half-written history row must not break the listing
                }
            }
            rows.Add(new AgentJobRow(rdr.GetString(0), enabled, outcome, when, rdr.GetString(2)));
        }
        return rows;
    }

    // -------------------------------------------------------------------------
    // Object dependencies
    // Runs in the connected database, because sys.sql_expression_dependencies is
    // a per-catalog view — there is no server-wide form of it.
    // -------------------------------------------------------------------------

    /// <summary>
    /// What the object reads and what reads it. Column-level references stay visible
    /// (<see cref="DependencyRow.Detail"/> carries the column) and foreign keys plus
    /// triggers are folded in, since the expression-dependency view does not record
    /// either of them.
    /// </summary>
    public async Task<ObjectDependencies> GetDependenciesAsync(
        ConnectionInfo info, string schema, string name, CancellationToken ct = default)
    {
        const string resolve = "SELECT o.object_id, o.type_desc FROM sys.objects AS o " +
                               "WHERE o.schema_id = SCHEMA_ID(@schema) AND o.name = @name";

        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);

        int objectId;
        string typeDesc;
        using (var findCmd = new SqlCommand(resolve, conn) { CommandTimeout = 15 })
        {
            findCmd.Parameters.AddWithValue("@schema", schema);
            findCmd.Parameters.AddWithValue("@name", name);
            await using var rdr = await findCmd.ExecuteReaderAsync(ct);
            if (!await rdr.ReadAsync(ct))
                throw new InvalidOperationException(
                    $"{schema}.{name} is not a table, view, function, procedure or trigger of {info.Database}.");
            objectId = rdr.GetInt32(0);
            typeDesc = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
        }

        const string query = """
            SELECT N'Uses' AS direction, QUOTENAME(rs.name) + N'.' + QUOTENAME(re.name) AS object_name,
                   re.type_desc AS object_type, ISNULL(COL_NAME(d.referenced_id, d.referenced_minor_id), N'') AS detail
            FROM sys.sql_expression_dependencies AS d
            JOIN sys.objects AS re ON re.object_id = d.referenced_id
            JOIN sys.schemas AS rs ON rs.schema_id = re.schema_id
            WHERE d.referencing_id = @oid AND d.is_ambiguous = 0
            UNION
            SELECT N'Uses',
                   ISNULL(QUOTENAME(ISNULL(NULLIF(d.referenced_database_name, N''), DB_NAME())) + N'.'
                          + QUOTENAME(d.referenced_entity_name), N'(unresolved)'),
                   N'EXTERNAL', ISNULL(d.referenced_schema_name, N'')
            FROM sys.sql_expression_dependencies AS d
            WHERE d.referencing_id = @oid AND d.referenced_id IS NULL
            UNION
            SELECT N'Uses', QUOTENAME(ps.name) + N'.' + QUOTENAME(pt.name), N'FOREIGN_KEY', f.name
            FROM sys.foreign_keys AS f
            JOIN sys.tables AS pt ON pt.object_id = f.referenced_object_id
            JOIN sys.schemas AS ps ON ps.schema_id = pt.schema_id
            WHERE f.parent_object_id = @oid
            UNION
            SELECT N'Used by', QUOTENAME(s.name) + N'.' + QUOTENAME(o.name), o.type_desc, N''
            FROM sys.sql_expression_dependencies AS d
            JOIN sys.objects AS o ON o.object_id = d.referencing_id
            JOIN sys.schemas AS s ON s.schema_id = o.schema_id
            WHERE d.referenced_id = @oid AND d.referencing_id <> @oid AND d.is_ambiguous = 0
            UNION
            SELECT N'Used by', QUOTENAME(cs.name) + N'.' + QUOTENAME(ctt.name), N'FOREIGN_KEY', f.name
            FROM sys.foreign_keys AS f
            JOIN sys.tables AS ctt ON ctt.object_id = f.parent_object_id
            JOIN sys.schemas AS cs ON cs.schema_id = ctt.schema_id
            WHERE f.referenced_object_id = @oid
            UNION
            SELECT N'Used by', QUOTENAME(tr.name), N'TRIGGER', N''
            FROM sys.triggers AS tr
            WHERE tr.parent_id = @oid
            ORDER BY 1, 2
            """;

        var rows = new List<DependencyRow>();
        await using (var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.AddWithValue("@oid", objectId);
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                rows.Add(new DependencyRow(
                    rdr.GetString(0), rdr.GetString(1),
                    rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                    rdr.IsDBNull(3) ? "" : rdr.GetString(3)));
        }

        // An object that references itself is not a dependency of anything.
        var self = $"[{schema}].[{name}]";
        rows.RemoveAll(r => string.Equals(r.ObjectName, self, StringComparison.OrdinalIgnoreCase));

        return new ObjectDependencies(schema, name, typeDesc, rows);
    }

    // -------------------------------------------------------------------------
    // Backup file inspection and safe restore
    // -------------------------------------------------------------------------

    private static string MasterConnectionFor(ConnectionInfo info) =>
        new SqlConnectionStringBuilder(info.ConnectionString) { InitialCatalog = "master" }.ConnectionString;

    /// <summary>RESTORE does not accept a parameterised device name, so the path is
    /// quoted and escaped here and validated before it ever reaches the string.</summary>
    private static string QuoteDevice(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose a backup file first.");
        if (path.Contains('\0') || Path.GetInvalidPathChars().Any(path.Contains))
            throw new InvalidOperationException("The backup file path contains invalid characters.");
        return "N'" + path.Replace("'", "''") + "'";
    }

    /// <summary>Every backup set stored in a media file, oldest first.</summary>
    public async Task<List<BackupSetInfo>> ReadBackupSetsAsync(
        ConnectionInfo info, string backupPath, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            $"RESTORE HEADERONLY FROM DISK = {QuoteDevice(backupPath)};", conn) { CommandTimeout = 60 };

        var sets = new List<BackupSetInfo>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            sets.Add(new BackupSetInfo
            {
                Position = HdrInt(rdr, "Position"),
                BackupType = HeaderBackupType(rdr),
                DatabaseName = HdrStr(rdr, "DatabaseName"),
                BackupStartDate = HdrTime(rdr, "BackupStartDate"),
                BackupSizeBytes = HdrLong(rdr, "BackupSize"),
                ServerName = HdrStr(rdr, "ServerName"),
                IsCopyOnly = HdrInt(rdr, "IsCopyOnly") != 0,
                Description = HdrStr(rdr, "BackupDescription")
            });
        }
        return sets;
    }

    /// <summary>The physical files contained in one backup set; the basis for MOVE.</summary>
    public async Task<List<BackupFileInfo>> ReadBackupFilesAsync(
        ConnectionInfo info, string backupPath, int setPosition, CancellationToken ct = default)
    {
        if (setPosition < 1)
            throw new InvalidOperationException("Select the backup set to inspect.");

        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            $"RESTORE FILELISTONLY FROM DISK = {QuoteDevice(backupPath)} WITH FILE = {setPosition};",
            conn) { CommandTimeout = 60 };

        var files = new List<BackupFileInfo>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            files.Add(new BackupFileInfo
            {
                LogicalName = HdrStr(rdr, "LogicalName"),
                PhysicalName = HdrStr(rdr, "PhysicalName"),
                Type = HdrChar(rdr, "Type"),
                SizeBytes = HdrLong(rdr, "Size"),
                MaxSizeBytes = HdrLong(rdr, "MaxSize"),
                GrowthBytes = HdrLong(rdr, "Growth"),
                FileGroupName = HdrStr(rdr, "FileGroupName")
            });
        }
        return files;
    }

    /// <summary>Where this instance puts new data and log files by default.</summary>
    public async Task<(string DataDirectory, string LogDirectory)> GetDefaultFileLocationsAsync(
        ConnectionInfo info, CancellationToken ct = default)
    {
        const string query = """
            SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(520)),
                   CAST(SERVERPROPERTY('InstanceDefaultLogPath')  AS nvarchar(520))
            """;
        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 15 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return ("", "");
        return (rdr.IsDBNull(0) ? "" : rdr.GetString(0), rdr.IsDBNull(1) ? "" : rdr.GetString(1));
    }

    /// <summary>
    /// The instance's default backup folder and the database's recovery model. A
    /// SIMPLE-recovery database has no log to back up, so the dialog hides that choice.
    /// </summary>
    public async Task<(string BackupDirectory, string RecoveryModel)> GetBackupDefaultsAsync(
        ConnectionInfo info, string database, CancellationToken ct = default)
    {
        const string query = """
            SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(520)),
                   ISNULL(DATABASEPROPERTYEX(@db, N'Recovery'), N'')
            """;
        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 15 };
        cmd.Parameters.Add("@db", SqlDbType.NVarChar, 128).Value = database;
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return ("", "");
        return (rdr.IsDBNull(0) ? "" : rdr.GetString(0), rdr.IsDBNull(1) ? "" : rdr.GetString(1));
    }

    public static async Task<bool> DatabaseExistsAsync(
        SqlConnection masterConnection, string database, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("SELECT DB_ID(@db);", masterConnection) { CommandTimeout = 15 };
        cmd.Parameters.Add("@db", SqlDbType.NVarChar, 128).Value = database;
        var id = await cmd.ExecuteScalarAsync(ct);
        return id is not null and not DBNull;
    }

    public async Task<bool> DatabaseExistsAsync(
        ConnectionInfo info, string database, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);
        return await DatabaseExistsAsync(conn, database, ct);
    }

    /// <summary>
    /// Runs a restore exactly as previewed. Never overwrites a live database unless
    /// the plan carries an explicit REPLACE decision, and only takes a database
    /// single-user when it is the database being replaced.
    /// </summary>
    public async Task RestoreDatabaseAsync(
        ConnectionInfo info, RestorePlan plan, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        plan.Validate();

        await using var conn = new SqlConnection(MasterConnectionFor(info));
        await conn.OpenAsync(ct);

        var exists = await DatabaseExistsAsync(conn, plan.TargetDatabase, ct);
        if (exists && !plan.ReplaceExisting)
            throw new InvalidOperationException(
                $"[{plan.TargetDatabase}] already exists on this instance. Either restore under a new " +
                "name or confirm that overwriting it is intended.");
        if (exists && plan.IsInPlaceRestore && plan.RelocatedCount == plan.Files.Count && plan.Files.Count > 0)
            progress?.Report("Moving every file: the original data and log files stay untouched.");

        var batches = ManagerScriptBuilder.RestoreBatches(plan, exists);
        for (var i = 0; i < batches.Count; i++)
        {
            progress?.Report(RestoreStepText(batches[i], plan));
            await using var cmd = new SqlCommand(batches[i], conn) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>Human-readable label for one restore batch, used for live status text.</summary>
    private static string RestoreStepText(string batch, RestorePlan plan)
    {
        if (batch.Contains("SET SINGLE_USER", StringComparison.OrdinalIgnoreCase))
            return $"Disconnecting other sessions on [{plan.TargetDatabase}]…";
        if (batch.Contains("SET MULTI_USER", StringComparison.OrdinalIgnoreCase))
            return $"Reopening [{plan.TargetDatabase}] to other connections…";
        var relocated = plan.RelocatedCount;
        return relocated > 0
            ? $"Restoring [{plan.TargetDatabase}] from set #{plan.SetPosition} — {relocated} file(s) relocated…"
            : $"Restoring [{plan.TargetDatabase}] from set #{plan.SetPosition} to its original file paths…";
    }

    // HEADERONLY / FILELISTONLY expose different columns across SQL Server
    // versions, so every field is read by name and tolerated when absent.
    private static int HdrOrdinal(SqlDataReader r, string name)
    {
        try { return r.GetOrdinal(name); } catch (IndexOutOfRangeException) { return -1; }
    }

    private static string HdrStr(SqlDataReader r, string name)
    {
        var i = HdrOrdinal(r, name);
        return i < 0 || r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    }

    private static long HdrLong(SqlDataReader r, string name)
    {
        var i = HdrOrdinal(r, name);
        return i < 0 || r.IsDBNull(i)
            ? 0
            : Convert.ToInt64(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int HdrInt(SqlDataReader r, string name) => (int)HdrLong(r, name);

    private static char HdrChar(SqlDataReader r, string name)
    {
        var value = HdrStr(r, name);
        return value.Length > 0 ? value[0] : ' ';
    }

    /// <summary>
    /// RESTORE HEADERONLY has no 'Type' column: its BackupType is a numeric id and
    /// BackupTypeDescription the prose name. The model keeps msdb's letters ('D', 'L', 'I'…).
    /// </summary>
    private static char HeaderBackupType(SqlDataReader r) => HdrInt(r, "BackupType") switch
    {
        1 => 'D',
        2 => 'L',
        4 => 'F',
        5 => 'I',
        6 => 'G',
        7 => 'P',
        8 => 'Q',
        _ => HdrStr(r, "BackupTypeDescription") switch
        {
            var d when d.Contains("Log", StringComparison.OrdinalIgnoreCase) => 'L',
            var d when d.StartsWith("Differential", StringComparison.OrdinalIgnoreCase) => 'I',
            var d when d.StartsWith("Database", StringComparison.OrdinalIgnoreCase) => 'D',
            _ => ' '
        }
    };

    private static DateTime HdrTime(SqlDataReader r, string name)
    {
        var i = HdrOrdinal(r, name);
        return i < 0 || r.IsDBNull(i) ? DateTime.MinValue : r.GetDateTime(i);
    }

    // -------------------------------------------------------------------------
    // Row count
    // -------------------------------------------------------------------------

    public async Task<long> GetRowCountAsync(
        ConnectionInfo info, string schema, string tableName, CancellationToken ct = default)
    {
        const string query = """
            SELECT SUM(p.rows)
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.partitions p ON t.object_id = p.object_id
            WHERE s.name = @schema AND t.name = @table AND p.index_id IN (0,1)
            """;
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.CommandTimeout = 15;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is DBNull or null ? 0L : Convert.ToInt64(result);
    }

    // -------------------------------------------------------------------------
    // Object Explorer filter helpers
    // -------------------------------------------------------------------------

    private static bool TypeMatchesFilter(DbObjectType objectType, ExplorerQueryFilter filter) =>
        filter.TypeLabel switch
        {
            null or "" => true,
            "Tables" => objectType == DbObjectType.Table,
            "Views" => objectType == DbObjectType.View,
            "Stored Procedures" => objectType == DbObjectType.StoredProcedure,
            "Functions" => objectType == DbObjectType.Function,
            "Triggers" => objectType == DbObjectType.Trigger,
            _ => true
        };

    /// <summary>Turns plain user text into a %…% LIKE pattern with SQL wildcards
    /// escaped via bracket-wrapping (ESCAPE '\' is not needed for [x] escaping).
    /// Returns null when the input is blank.</summary>
    private static string? LikePattern(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            if (ch is '%' or '_' or '[' or ']')
                sb.Append('[').Append(ch).Append(']');
            else
                sb.Append(ch);
        }
        return "%" + sb + "%";
    }

    /// <summary>Injects an AND (name LIKE … OR schema LIKE …) predicate before the
    /// ORDER BY of the per-type queries used by <see cref="GetObjectsAsync"/>.</summary>
    private static string AddLikePredicate(string query, DbObjectType objectType, bool onName, bool onSchema)
    {
        var (nameCol, schemaCol) = objectType switch
        {
            DbObjectType.Table or DbObjectType.View => ("TABLE_NAME", "TABLE_SCHEMA"),
            DbObjectType.StoredProcedure or DbObjectType.Function => ("SPECIFIC_NAME", "SPECIFIC_SCHEMA"),
            _ => ("t.name", "s.name") // Trigger — aliases in its own query
        };
        var ors = new List<string>();
        if (onName) ors.Add($"{nameCol} LIKE @objName");
        if (onSchema) ors.Add($"{schemaCol} LIKE @objSchema");

        var orderIdx = query.LastIndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        var head = orderIdx < 0 ? query : query[..orderIdx];
        var tail = orderIdx < 0 ? "" : query[orderIdx..];
        // INFORMATION_SCHEMA.VIEWS and the trigger query have no WHERE clause of their
        // own, so appending "AND …" there is a syntax error at the FROM line.
        var hasWhere = head.Contains("WHERE", StringComparison.OrdinalIgnoreCase);
        var predicate = "(" + string.Join(" OR ", ors) + ")";
        return head + (hasWhere ? " AND " : " WHERE ") + predicate + "\n" + tail;
    }

    // -------------------------------------------------------------------------
    // Table properties (SSMS-style read-only dialog)
    // -------------------------------------------------------------------------

    /// <summary>Full per-table property snapshot: space usage (sys.dm_db_partition_stats
    /// + sys.allocation_units, the same arithmetic sp_spaceused uses), columns,
    /// primary key, foreign keys in both directions, check constraints, indexes with
    /// usage stats, triggers, statistics and partitioning. DMV-backed sections degrade
    /// to empty/plain data when the DMVs are not accessible.</summary>
    public async Task<TableProperties> GetTablePropertiesAsync(
        ConnectionInfo info, string schema, string tableName, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);

        long reservedKb = 0, dataKb = 0, indexKb = 0, unusedKb = 0, rows = 0;
        await using (var cmd = new SqlCommand("""
            SELECT COALESCE(SUM(a.total_pages), 0) * 8,
                   COALESCE(SUM(CASE WHEN a.type_desc = N'INDEX' THEN 0 ELSE a.data_pages END), 0) * 8,
                   COALESCE(SUM(a.used_pages
                                - CASE WHEN a.type_desc = N'INDEX' THEN 0 ELSE a.data_pages END), 0) * 8,
                   COALESCE((SELECT SUM(p.rows) FROM sys.partitions p
                             JOIN sys.tables t2 ON t2.object_id = p.object_id
                             JOIN sys.schemas s2 ON t2.schema_id = s2.schema_id
                             WHERE s2.name = @schema AND t2.name = @table AND p.index_id IN (0, 1)), 0)
            FROM sys.allocation_units a
            JOIN sys.partitions p ON p.hobt_id = a.container_id
            JOIN sys.tables t ON t.object_id = p.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table
            """, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.Add("@schema", SqlDbType.NVarChar, 128).Value = schema;
            cmd.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = tableName;
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            if (await rdr.ReadAsync(ct))
            {
                reservedKb = Convert.ToInt64(rdr.GetValue(0), System.Globalization.CultureInfo.InvariantCulture);
                dataKb     = Convert.ToInt64(rdr.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
                indexKb    = Convert.ToInt64(rdr.GetValue(2), System.Globalization.CultureInfo.InvariantCulture);
                rows       = Convert.ToInt64(rdr.GetValue(3), System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        unusedKb = Math.Max(0, reservedKb - dataKb - indexKb);

        var columns = await GetTableColumnsAsync(info, schema, tableName, ct);

        TablePkInfo? pk = null;
        await using (var cmd = new SqlCommand("""
            SELECT kc.name,
                   STUFF((SELECT ', ' + c.name
                          FROM sys.index_columns ic
                          JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                          WHERE ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                          ORDER BY ic.key_ordinal
                          FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '')
            FROM sys.key_constraints kc
            JOIN sys.tables t ON kc.parent_object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE kc.type = 'PK' AND s.name = @schema AND t.name = @table
            """, conn) { CommandTimeout = 30 })
        {
            AddTableParams(cmd, schema, tableName);
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            if (await rdr.ReadAsync(ct))
                pk = new TablePkInfo
                {
                    Name = rdr.GetString(0),
                    Columns = SplitColumns(rdr.IsDBNull(1) ? "" : rdr.GetString(1))
                };
        }

        var fks = await ReadForeignKeysAsync(conn, schema, tableName, outbound: true, ct);
        var inbound = await ReadForeignKeysAsync(conn, schema, tableName, outbound: false, ct);

        var checks = new List<TableCheckRow>();
        await using (var cmd = new SqlCommand("""
            SELECT kc.name, CAST(kc.definition AS nvarchar(4000))
            FROM sys.check_constraints kc
            JOIN sys.tables t ON kc.parent_object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY kc.name
            """, conn) { CommandTimeout = 30 })
        {
            AddTableParams(cmd, schema, tableName);
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                checks.Add(new TableCheckRow
                {
                    Name = rdr.GetString(0),
                    Definition = rdr.IsDBNull(1) ? "" : rdr.GetString(1)
                });
        }

        var indexes = await ReadIndexRowsAsync(conn, schema, tableName, ct);

        var triggers = new List<TableTriggerRow>();
        await using (var cmd = new SqlCommand("""
            SELECT tr.name,
                   CASE WHEN tr.is_instead_of_trigger = 1 THEN N'INSTEAD OF' ELSE N'AFTER' END,
                   COALESCE((SELECT CAST(STUFF((SELECT ', ' + te.type_desc
                                                 FROM sys.trigger_events te
                                                 WHERE te.object_id = tr.object_id
                                                 FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '')
                                   AS nvarchar(max))), ''),
                   tr.is_disabled
            FROM sys.triggers tr
            JOIN sys.tables t ON tr.parent_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY tr.name
            """, conn) { CommandTimeout = 30 })
        {
            AddTableParams(cmd, schema, tableName);
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                triggers.Add(new TableTriggerRow
                {
                    Name = rdr.GetString(0),
                    Timing = rdr.GetString(1),
                    Events = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                    IsDisabled = rdr.GetBoolean(3)
                });
        }

        var stats = await ReadStatRowsAsync(conn, schema, tableName, ct);

        TablePartitionInfo? partitioning = null;
        await using (var cmd = new SqlCommand("""
            SELECT TOP (1) ps.name, pf.name, pf.type_desc
            FROM sys.partitions p
            JOIN sys.tables t ON t.object_id = p.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id
            JOIN sys.partition_schemes ps ON i.data_space_id = ps.data_space_id
            JOIN sys.partition_functions pf ON ps.function_id = pf.function_id
            WHERE s.name = @schema AND t.name = @table AND p.index_id IN (0, 1)
            """, conn) { CommandTimeout = 30 })
        {
            AddTableParams(cmd, schema, tableName);
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            if (await rdr.ReadAsync(ct))
                partitioning = new TablePartitionInfo
                {
                    SchemeName = rdr.GetString(0),
                    FunctionName = rdr.GetString(1),
                    FunctionTypeDesc = rdr.GetString(2)
                };
        }

        var partitions = new List<MetaPartition>();
        await using (var cmd = new SqlCommand("""
            SELECT p.partition_number, p.rows,
                   pf.name, ps.name,
                   CAST(rv.value AS NVARCHAR(4000)),
                   COALESCE(fg.name, fg0.name)
            FROM sys.partitions p
            JOIN sys.tables t ON p.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            LEFT JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id
            LEFT JOIN sys.partition_schemes ps ON i.data_space_id = ps.data_space_id
            LEFT JOIN sys.partition_functions pf ON ps.function_id = pf.function_id
            LEFT JOIN sys.partition_range_values rv ON rv.function_id = pf.function_id AND rv.boundary_id = p.partition_number
            LEFT JOIN sys.destination_data_spaces dds ON dds.partition_scheme_id = ps.data_space_id AND dds.destination_id = p.partition_number
            LEFT JOIN sys.filegroups fg ON fg.data_space_id = dds.data_space_id
            LEFT JOIN sys.filegroups fg0 ON fg0.data_space_id = i.data_space_id
            WHERE s.name = @schema AND t.name = @table AND p.index_id IN (0, 1)
            ORDER BY p.partition_number
            """, conn) { CommandTimeout = 30 })
        {
            AddTableParams(cmd, schema, tableName);
            try
            {
                await using var rdr = await cmd.ExecuteReaderAsync(ct);
                while (await rdr.ReadAsync(ct))
                    partitions.Add(new MetaPartition(
                        rdr.GetInt32(0),
                        rdr.GetInt64(1),
                        rdr.IsDBNull(2) ? null : rdr.GetString(2),
                        rdr.IsDBNull(3) ? null : rdr.GetString(3),
                        rdr.IsDBNull(4) ? null : rdr.GetString(4),
                        rdr.IsDBNull(5) ? null : rdr.GetString(5)));
            }
            catch (SqlException) { partitions.Clear(); }
        }

        return new TableProperties
        {
            Schema = schema,
            Name = tableName,
            Rows = rows,
            ReservedKb = reservedKb,
            DataKb = dataKb,
            IndexKb = indexKb,
            UnusedKb = unusedKb,
            Columns = columns,
            PrimaryKey = pk,
            ForeignKeys = fks,
            ReferencedBy = inbound,
            Checks = checks,
            Indexes = indexes,
            Triggers = triggers,
            Statistics = stats,
            Partitioning = partitioning,
            Partitions = partitions
        };
    }

    private static void AddTableParams(SqlCommand cmd, string schema, string table)
    {
        cmd.Parameters.Add("@schema", SqlDbType.NVarChar, 128).Value = schema;
        cmd.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
    }

    /// <summary>Strip the bracket wrapping SQL Server puts on column lists built
    /// via QUOTENAME-style metadata helpers.</summary>
    private static List<string> SplitColumns(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Select(c => c.TrimStart('[').TrimEnd(']'))
           .ToList();

    private static async Task<List<TableForeignKeyRow>> ReadForeignKeysAsync(
        SqlConnection conn, string schema, string tableName, bool outbound, CancellationToken ct)
    {
        var query = outbound
            ? """
              SELECT fk.name,
                     (SELECT CAST(STUFF((SELECT ', ' + c.name
                                         FROM sys.foreign_key_columns fkc
                                         JOIN sys.columns c ON c.object_id = fkc.parent_object_id
                                                  AND c.column_id = fkc.parent_column_id
                                         WHERE fkc.constraint_object_id = fk.object_id
                                         ORDER BY fkc.constraint_column_id
                                         FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '')
                                  AS nvarchar(max))),
                     ref_s.name + '.' + rt.name,
                     (SELECT CAST(STUFF((SELECT ', ' + c.name
                                         FROM sys.foreign_key_columns fkc
                                         JOIN sys.columns c ON c.object_id = fkc.referenced_object_id
                                                  AND c.column_id = fkc.referenced_column_id
                                         WHERE fkc.constraint_object_id = fk.object_id
                                         ORDER BY fkc.constraint_column_id
                                         FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '')
                                  AS nvarchar(max)))
              FROM sys.foreign_keys fk
              JOIN sys.tables t ON fk.parent_object_id = t.object_id
              JOIN sys.schemas s ON t.schema_id = s.schema_id
              JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
              JOIN sys.schemas ref_s ON rt.schema_id = ref_s.schema_id
              WHERE s.name = @schema AND t.name = @table
              ORDER BY fk.name
              """
            : """
              SELECT fk.name,
                     (SELECT CAST(STUFF((SELECT ', ' + c.name
                                         FROM sys.foreign_key_columns fkc
                                         JOIN sys.columns c ON c.object_id = fkc.referenced_object_id
                                                  AND c.column_id = fkc.referenced_column_id
                                         WHERE fkc.constraint_object_id = fk.object_id
                                         ORDER BY fkc.constraint_column_id
                                         FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '')
                                  AS nvarchar(max))),
                     p_s.name + '.' + pt.name,
                     (SELECT CAST(STUFF((SELECT ', ' + c.name
                                         FROM sys.foreign_key_columns fkc
                                         JOIN sys.columns c ON c.object_id = fkc.parent_object_id
                                                  AND c.column_id = fkc.parent_column_id
                                         WHERE fkc.constraint_object_id = fk.object_id
                                         ORDER BY fkc.constraint_column_id
                                         FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '')
                                  AS nvarchar(max)))
              FROM sys.foreign_keys fk
              JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
              JOIN sys.schemas s ON rt.schema_id = s.schema_id
              JOIN sys.tables pt ON fk.parent_object_id = pt.object_id
              JOIN sys.schemas p_s ON pt.schema_id = p_s.schema_id
              WHERE s.name = @schema AND rt.name = @table
              ORDER BY pt.name, fk.name
              """;

        var list = new List<TableForeignKeyRow>();
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
        AddTableParams(cmd, schema, tableName);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var cols = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
            var other = rdr.IsDBNull(2) ? "" : rdr.GetString(2);
            var otherCols = rdr.IsDBNull(3) ? "" : rdr.GetString(3);
            list.Add(outbound
                ? new TableForeignKeyRow
                  {
                      Name = rdr.GetString(0), Columns = cols,
                      ReferencedTable = other, ReferencedColumns = otherCols
                  }
                : new TableForeignKeyRow
                  {
                      Name = rdr.GetString(0), Columns = cols,
                      ParentTable = other, ParentColumns = otherCols
                  });
        }
        return list;
    }

    private static async Task<List<TableIndexRow>> ReadIndexRowsAsync(
        SqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string baseCols = """
                   i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_disabled,
                   (SELECT CAST(STUFF((SELECT ', ' + c.name
                                       FROM sys.index_columns ic
                                       JOIN sys.columns c ON c.object_id = ic.object_id
                                                AND c.column_id = ic.column_id
                                       WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                                         AND ic.is_included_column = 0
                                       ORDER BY ic.key_ordinal
                                       FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '')
                                AS nvarchar(max))),
                   (SELECT CAST(STUFF((SELECT ', ' + c.name
                                       FROM sys.index_columns ic
                                       JOIN sys.columns c ON c.object_id = ic.object_id
                                                AND c.column_id = ic.column_id
                                       WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                                         AND ic.is_included_column = 1
                                       ORDER BY ic.index_column_id
                                       FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '')
                                AS nvarchar(max)))
              """;
        var withUsage = $"""
            SELECT {baseCols},
                   COALESCE(us.user_seeks, 0), COALESCE(us.user_scans, 0),
                   COALESCE(us.user_lookups, 0), COALESCE(us.user_updates, 0)
            FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            LEFT JOIN sys.dm_db_index_usage_stats us
                   ON us.object_id = i.object_id AND us.index_id = i.index_id AND us.database_id = DB_ID()
            WHERE s.name = @schema AND t.name = @table AND i.name IS NOT NULL
            ORDER BY i.name
            """;
        var withoutUsage = $"""
            SELECT {baseCols}, 0, 0, 0, 0
            FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table AND i.name IS NOT NULL
            ORDER BY i.name
            """;

        foreach (var query in new[] { withUsage, withoutUsage })
        {
            var list = new List<TableIndexRow>();
            try
            {
                await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
                AddTableParams(cmd, schema, tableName);
                await using var rdr = await cmd.ExecuteReaderAsync(ct);
                while (await rdr.ReadAsync(ct))
                {
                    list.Add(new TableIndexRow
                    {
                        Name = rdr.GetString(0),
                        TypeDesc = rdr.GetString(1),
                        IsUnique = rdr.GetBoolean(2),
                        IsPrimaryKey = rdr.GetBoolean(3),
                        IsDisabled = rdr.GetBoolean(4),
                        KeyColumns = rdr.IsDBNull(5) ? "" : rdr.GetString(5),
                        IncludedColumns = rdr.IsDBNull(6) ? "" : rdr.GetString(6),
                        UserSeeks = Convert.ToInt64(rdr.GetValue(7), System.Globalization.CultureInfo.InvariantCulture),
                        UserScans = Convert.ToInt64(rdr.GetValue(8), System.Globalization.CultureInfo.InvariantCulture),
                        UserLookups = Convert.ToInt64(rdr.GetValue(9), System.Globalization.CultureInfo.InvariantCulture),
                        UserUpdates = Convert.ToInt64(rdr.GetValue(10), System.Globalization.CultureInfo.InvariantCulture)
                    });
                }
            }
            catch (SqlException)
            {
                if (query == withUsage) continue; // retry without the DMV join
                throw;
            }
            return list;
        }
        return [];
    }

    private static async Task<List<TableStatRow>> ReadStatRowsAsync(
        SqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string colList = """
                   (SELECT CAST(STUFF((SELECT ', ' + c.name
                                       FROM sys.stats_columns sc
                                       JOIN sys.columns c ON c.object_id = sc.object_id
                                                AND c.column_id = sc.column_id
                                       WHERE sc.object_id = st.object_id AND sc.stats_id = st.stats_id
                                       ORDER BY sc.stats_column_id
                                       FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '')
                                AS nvarchar(max)))
              """;
        var withProps = $"""
            SELECT st.name, {colList},
                   COALESCE(sp.rows, 0), st.has_filter,
                   STATS_DATE(st.object_id, st.stats_id)
            FROM sys.stats st
            JOIN sys.tables t ON st.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            OUTER APPLY sys.dm_db_stats_properties(st.object_id, st.stats_id) sp
            WHERE s.name = @schema AND t.name = @table
            ORDER BY st.name
            """;
        var plain = $"""
            SELECT st.name, {colList},
                   0, st.has_filter,
                   STATS_DATE(st.object_id, st.stats_id)
            FROM sys.stats st
            JOIN sys.tables t ON st.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY st.name
            """;

        foreach (var query in new[] { withProps, plain })
        {
            var list = new List<TableStatRow>();
            try
            {
                await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
                AddTableParams(cmd, schema, tableName);
                await using var rdr = await cmd.ExecuteReaderAsync(ct);
                while (await rdr.ReadAsync(ct))
                {
                    list.Add(new TableStatRow
                    {
                        Name = rdr.GetString(0),
                        Columns = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                        Rows = Convert.ToInt64(rdr.GetValue(2), System.Globalization.CultureInfo.InvariantCulture),
                        Filtered = rdr.GetBoolean(3),
                        LastUpdated = rdr.IsDBNull(4) ? null : Convert.ToDateTime(rdr.GetValue(4), System.Globalization.CultureInfo.InvariantCulture)
                    });
                }
            }
            catch (SqlException)
            {
                if (query == withProps) continue; // retry without dm_db_stats_properties
                throw;
            }
            return list;
        }
        return [];
    }
}
