using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Optional enrichments for the Query Constructor: best-effort FK column pairs
/// (used to pre-fill the JOIN ON clause), and per-table column lists (used to
/// populate the column dropdowns after the user picks tables).
///
/// Everything falls back to "no suggestions" on error — the UI is fully usable
/// without FK metadata.
/// </summary>
public sealed class QueryBuilderService
{
    /// <summary>
    /// One candidate for the JOIN ON clause. Key is "leftAlias.leftCol";
    /// the JOIN UI looks these up by the right table the user picked.
    /// </summary>
    public sealed record JoinSuggestion(string LeftSchema, string LeftTable, string LeftColumn,
                                        string RightSchema, string RightTable, string RightColumn);

    /// <summary>
    /// Returns FK pairs where the *left* side is <paramref name="leftSchema"/>.<paramref name="leftTable"/>.
    /// The result can be used to pre-fill the JOIN ON clause when the user adds a
    /// right table that is referenced by an FK from the current primary/left table.
    /// </summary>
    public async Task<List<JoinSuggestion>> GetJoinSuggestionsAsync(
        ConnectionInfo info, string leftSchema, string leftTable, CancellationToken ct = default)
    {
        var list = new List<JoinSuggestion>();
        try
        {
            const string sql = """
                SELECT
                    s1.name AS left_schema,  t1.name AS left_table,  c1.name AS left_column,
                    s2.name AS right_schema, t2.name AS right_table, c2.name AS right_column
                FROM sys.foreign_key_columns fkc
                JOIN sys.tables t1 ON fkc.parent_object_id = t1.object_id
                JOIN sys.schemas s1 ON t1.schema_id = s1.schema_id
                JOIN sys.columns c1 ON c1.object_id = t1.object_id AND c1.column_id = fkc.parent_column_id
                JOIN sys.tables t2 ON fkc.referenced_object_id = t2.object_id
                JOIN sys.schemas s2 ON t2.schema_id = s2.schema_id
                JOIN sys.columns c2 ON c2.object_id = t2.object_id AND c2.column_id = fkc.referenced_column_id
                WHERE s1.name = @ls AND t1.name = @lt
                ORDER BY t2.name, c1.column_id
                """;
            await using var conn = new SqlConnection(info.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
            cmd.Parameters.AddWithValue("@ls", leftSchema);
            cmd.Parameters.AddWithValue("@lt", leftTable);
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                list.Add(new JoinSuggestion(
                    rdr.GetString(0), rdr.GetString(1), rdr.GetString(2),
                    rdr.GetString(3), rdr.GetString(4), rdr.GetString(5)));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[QueryBuilder] FK lookup failed: {ex.Message}");
        }
        return list;
    }

    /// <summary>
    /// Loads every column for every table in <paramref name="tables"/>, using the
    /// schema cache already populated by <see cref="QuerySchemaService"/>. This is
    /// synchronous: it just reads the static cache, no DB round-trip.
    /// </summary>
    public static void PopulateColumns(IEnumerable<BuilderTable> tables,
                                       Dictionary<string, List<string>> columnsByTable)
    {
        foreach (var t in tables)
        {
            t.Columns.Clear();
            var key = $"{t.Schema}.{t.Name}";
            if (columnsByTable.TryGetValue(key, out var cols) || columnsByTable.TryGetValue(t.Name, out cols))
                foreach (var c in cols) t.Columns.Add(c);
            else
                t.Columns.Add("(no columns cached)");
        }
    }
}