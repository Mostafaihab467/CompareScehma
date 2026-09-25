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

    /// <summary>Returns all objects of the requested type from the database.</summary>
    public async Task<List<DbObjectInfo>> GetObjectsAsync(
        ConnectionInfo info, DbObjectType objectType, CancellationToken ct = default)
    {
        var results = new List<DbObjectInfo>();

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

        await using var conn = new SqlConnection(info.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn);
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
        const string query = """
            SELECT
                p.name,
                TYPE_NAME(p.user_type_id),
                p.is_output,
                p.has_default_value,
                CAST(p.default_value AS NVARCHAR(256))
            FROM sys.parameters p
            JOIN sys.objects o ON p.object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
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

    /// <summary>Restores the connected database from a .bak file, forcing
    /// exclusive access. Runs from master so the target's own connections
    /// (including this session's context) never block the restore.
    /// No command timeout — backups can take as long as they take.</summary>
    public async Task RestoreDatabaseAsync(
        ConnectionInfo info, string backupPath, CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder(info.ConnectionString)
        {
            InitialCatalog = "master"
        };
        var db = info.Database.Replace("]", "]]");
        var path = backupPath.Replace("'", "''");

        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);
        foreach (var batch in new[]
                 {
                     $"ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;",
                     $"RESTORE DATABASE [{db}] FROM DISK = N'{path}' WITH REPLACE, RECOVERY;",
                     $"ALTER DATABASE [{db}] SET MULTI_USER;"
                 })
        {
            await using var cmd = new SqlCommand(batch, conn) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct);
        }
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
}
