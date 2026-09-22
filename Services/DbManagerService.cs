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
