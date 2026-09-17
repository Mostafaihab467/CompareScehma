using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Synchronizes rows from a source SQL Server database into an existing, compatible target.
/// It deliberately never deletes target rows, and requires a PK for deterministic upserts.
/// </summary>
public sealed class DataMoveService
{
    public async Task<DataMovePlan> AnalyzeAsync(ConnectionInfo source, ConnectionInfo target, IProgress<string>? progress = null)
    {
        progress?.Report("Reading source tables and primary keys...");
        var sourceTables = await ReadTablesAsync(source.ConnectionString);
        var sourceCounts = await ReadRowCountsAsync(source.ConnectionString);
        progress?.Report("Checking matching target tables...");
        var targetTables = await ReadTablesAsync(target.ConnectionString);
        var targetByName = targetTables.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        var result = new List<DataMoveTable>();
        foreach (var item in sourceTables.OrderBy(x => x.Value.Schema).ThenBy(x => x.Value.Name))
        {
            var s = item.Value;
            string? warning = null;
            if (!targetByName.TryGetValue(item.Key, out var t)) warning = "Missing on target";
            else if (s.PrimaryKeys.Count == 0 || t.Value.PrimaryKeys.Count == 0) warning = "Primary key required for safe update";
            else if (s.WritableColumns.Count == 0 || !s.PrimaryKeysNames.All(c => s.WritableColumns.Contains(c, StringComparer.OrdinalIgnoreCase))) warning = "Primary key contains a non-copyable column";
            else if (!s.PrimaryKeysNames.SequenceEqual(t.Value.PrimaryKeysNames, StringComparer.OrdinalIgnoreCase)) warning = "Primary key differs on target";
            else if (!s.WritableColumns.All(c => t.Value.WritableColumns.Contains(c, StringComparer.OrdinalIgnoreCase))) warning = "Target is missing writable columns";

            sourceCounts.TryGetValue(item.Key, out var count);
            result.Add(new DataMoveTable { Schema = s.Schema, Name = s.Name,
                WritableColumns = s.WritableColumns, PrimaryKeyColumns = s.PrimaryKeysNames, SourceRowCount = count, Warning = warning, IsSelected = warning is null });
        }

        progress?.Report("Reading target foreign-key relationships...");
        // Target constraints are authoritative: these are the rules an INSERT must satisfy.
        var relations = await ReadRelationsAsync(target.ConnectionString);
        var parents = relations.GroupBy(r => r.ChildTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ParentTable).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        progress?.Report($"Analysis finished: {result.Count(x => x.Warning is null)} compatible table(s), {result.Count(x => x.Warning is not null)} blocked.");
        return new DataMovePlan { Tables = result, ParentTables = parents, Relations = relations };
    }

    public async Task<DataMoveResult> SyncAsync(ConnectionInfo source, ConnectionInfo target, IEnumerable<DataMoveTable> selected,
        DataMovePlan plan, IProgress<string>? progress = null)
    {
        var tables = selected.Where(t => t.Warning is null).ToList();
        if (tables.Count == 0) throw new InvalidOperationException("Select at least one compatible table.");
        var order = GetDependencyOrder(tables, plan.ParentTables);
        progress?.Report("Validating selected table relationships before writing data...");
        await ValidateSourceRelationshipsAsync(source.ConnectionString, order, plan.Relations, progress);
        progress?.Report("Parent-first copy sequence: " + string.Join(" → ", order.Select(t => $"{t.Schema}.{t.Name}")));
        long inserted = 0, updated = 0;
        await using var targetConnection = new SqlConnection(target.ConnectionString);
        await targetConnection.OpenAsync();
        for (var index = 0; index < order.Count; index++)
        {
            var table = order[index];
            // Verify the actual target state after all preceding parents were synchronized.
            // This turns a raw FK INSERT exception into a precise relationship diagnostic.
            await ValidateTargetParentsForChildAsync(source.ConnectionString, targetConnection, table, plan.Relations);
            progress?.Report($"[{index + 1}/{order.Count}] Copying {table.FullName} ({table.SourceRowCount:N0} source rows)...");
            var result = await SyncTableAsync(source.ConnectionString, targetConnection, table);
            inserted += result.Inserted;
            updated += result.Updated;
            progress?.Report($"[{index + 1}/{order.Count}] {table.FullName}: inserted {result.Inserted:N0}, updated {result.Updated:N0}.");
        }
        return new DataMoveResult(order.Count, inserted, updated);
    }

    private static async Task<(long Inserted, long Updated)> SyncTableAsync(string sourceCs, SqlConnection target, DataMoveTable table)
    {
        var quotedTable = table.FullName;
        var columns = table.WritableColumns.Select(Q).ToList();
        var pk = table.PrimaryKeyColumns.Select(Q).ToList();
        var nonPk = table.WritableColumns.Where(c => !table.PrimaryKeyColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).Select(Q).ToList();
        await using var tx = await target.BeginTransactionAsync();
        try
        {
            var tempName = "#DataMoveStage";
            await ExecuteAsync(target, tx, $"SELECT TOP (0) {string.Join(", ", columns)} INTO {tempName} FROM {quotedTable};");
            await using (var sourceConnection = new SqlConnection(sourceCs))
            {
                await sourceConnection.OpenAsync();
                await using var read = new SqlCommand($"SELECT {string.Join(", ", columns)} FROM {quotedTable};", sourceConnection);
                await using var reader = await read.ExecuteReaderAsync();
                // SELECT INTO can carry an IDENTITY property into the local staging table.
                // KeepIdentity is essential here: generated staging IDs would otherwise replace
                // source UserID values and break parent/child FK references on the target.
                using var bulk = new SqlBulkCopy(target, SqlBulkCopyOptions.KeepIdentity, (SqlTransaction)tx) { DestinationTableName = tempName, BatchSize = 5000, BulkCopyTimeout = 0 };
                foreach (var col in columns) bulk.ColumnMappings.Add(col.Trim('[', ']'), col.Trim('[', ']'));
                await bulk.WriteToServerAsync(reader);
            }
            var join = string.Join(" AND ", pk.Select(c => $"T.{c} = S.{c}"));
            long updated = 0;
            if (nonPk.Count > 0)
                updated = await ScalarAsync(target, tx, $"UPDATE T SET {string.Join(", ", nonPk.Select(c => $"T.{c} = S.{c}"))} FROM {quotedTable} T JOIN {tempName} S ON {join}; SELECT @@ROWCOUNT;");
            var identity = await ScalarAsync(target, tx, $"SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID(N'{table.Schema.Replace("'", "''")}.{table.Name.Replace("'", "''")}');") > 0;
            if (identity) await ExecuteAsync(target, tx, $"SET IDENTITY_INSERT {quotedTable} ON;");
            long inserted;
            try { inserted = await ScalarAsync(target, tx, $"INSERT INTO {quotedTable} ({string.Join(", ", columns)}) SELECT {string.Join(", ", columns.Select(c => $"S.{c}"))} FROM {tempName} S WHERE NOT EXISTS (SELECT 1 FROM {quotedTable} T WHERE {join}); SELECT @@ROWCOUNT;"); }
            finally { if (identity) await ExecuteAsync(target, tx, $"SET IDENTITY_INSERT {quotedTable} OFF;"); }
            // A local temporary table lives for the whole SQL connection, not merely this
            // transaction. Remove it explicitly so the next selected table gets a clean stage.
            await ExecuteAsync(target, tx, $"DROP TABLE {tempName};");
            await tx.CommitAsync();
            return (inserted, updated);
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    private static async Task ExecuteAsync(SqlConnection c, DbTransaction tx, string sql) { await using var cmd = new SqlCommand(sql, c, (SqlTransaction)tx) { CommandTimeout = 0 }; await cmd.ExecuteNonQueryAsync(); }
    private static async Task<long> ScalarAsync(SqlConnection c, DbTransaction tx, string sql) { await using var cmd = new SqlCommand(sql, c, (SqlTransaction)tx) { CommandTimeout = 0 }; return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
    private static string Q(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static async Task<Dictionary<string, long>> ReadRowCountsAsync(string cs)
    {
        const string sql = """
SELECT s.name, t.name, SUM(ps.rows)
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
LEFT JOIN sys.partitions ps ON ps.object_id=t.object_id AND ps.index_id IN (0,1)
WHERE t.is_ms_shipped=0 GROUP BY s.name,t.name;
""";
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var c = new SqlConnection(cs); await c.OpenAsync(); await using var cmd = new SqlCommand(sql, c); await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) result[$"{r.GetString(0)}.{r.GetString(1)}"] = r.IsDBNull(2) ? 0 : r.GetInt64(2);
        return result;
    }

    private static async Task<Dictionary<string, TableInfo>> ReadTablesAsync(string cs)
    {
        const string sql = """
SELECT s.name, t.name, c.name, c.column_id, c.is_identity, c.is_computed, ty.name,
       CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END AS IsPk, pk.key_ordinal
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
LEFT JOIN (SELECT ic.object_id,ic.column_id,ic.key_ordinal FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id WHERE i.is_primary_key=1) pk ON pk.object_id=c.object_id AND pk.column_id=c.column_id
WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name,c.column_id;
""";
        var map = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        await using var c = new SqlConnection(cs); await c.OpenAsync(); await using var cmd = new SqlCommand(sql, c); await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) { var s=r.GetString(0); var n=r.GetString(1); var key=$"{s}.{n}"; if (!map.TryGetValue(key,out var t)) map[key]=t=new TableInfo(s,n); var col=r.GetString(2); if (!r.GetBoolean(5) && !string.Equals(r.GetString(6),"timestamp",StringComparison.OrdinalIgnoreCase) && !string.Equals(r.GetString(6),"rowversion",StringComparison.OrdinalIgnoreCase)) t.WritableColumns.Add(col); if (Convert.ToInt32(r.GetValue(7)) == 1) t.PrimaryKeys.Add((Convert.ToInt32(r.GetValue(8)),col)); }
        foreach (var t in map.Values) t.PrimaryKeys.Sort((a,b)=>a.Ordinal.CompareTo(b.Ordinal));
        return map;
    }
    private static async Task<List<DataMoveRelation>> ReadRelationsAsync(string cs)
    {
        const string sql = """
SELECT f.name, cs.name, ct.name, ps.name, pt.name, cc.name, pc.name, fkc.constraint_column_id
FROM sys.foreign_keys f
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=f.object_id
JOIN sys.tables ct ON ct.object_id=f.parent_object_id JOIN sys.schemas cs ON cs.schema_id=ct.schema_id
JOIN sys.tables pt ON pt.object_id=f.referenced_object_id JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
JOIN sys.columns cc ON cc.object_id=ct.object_id AND cc.column_id=fkc.parent_column_id
JOIN sys.columns pc ON pc.object_id=pt.object_id AND pc.column_id=fkc.referenced_column_id
ORDER BY f.name, fkc.constraint_column_id;
""";
        var result = new List<DataMoveRelation>();
        await using var c = new SqlConnection(cs); await c.OpenAsync(); await using var cmd = new SqlCommand(sql, c); await using var r = await cmd.ExecuteReaderAsync();
        DataMoveRelation? current = null;
        while (await r.ReadAsync())
        {
            var name = r.GetString(0);
            if (current is null || !string.Equals(current.ConstraintName, name, StringComparison.OrdinalIgnoreCase))
            {
                current = new DataMoveRelation { ConstraintName = name, ChildTable = $"{r.GetString(1)}.{r.GetString(2)}", ParentTable = $"{r.GetString(3)}.{r.GetString(4)}", ChildColumns = [], ParentColumns = [] };
                result.Add(current);
            }
            current.ChildColumns.Add(r.GetString(5));
            current.ParentColumns.Add(r.GetString(6));
        }
        return result;
    }

    private static async Task ValidateSourceRelationshipsAsync(string sourceCs, IReadOnlyCollection<DataMoveTable> order, IReadOnlyCollection<DataMoveRelation> relations, IProgress<string>? progress)
    {
        var selected = order.Select(t => $"{t.Schema}.{t.Name}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relevant = relations.Where(r => selected.Contains(r.ChildTable) && selected.Contains(r.ParentTable)).ToList();
        await using var c = new SqlConnection(sourceCs); await c.OpenAsync();
        foreach (var relation in relevant)
        {
            var child = ToQuotedTable(relation.ChildTable);
            var parent = ToQuotedTable(relation.ParentTable);
            var join = string.Join(" AND ", relation.ChildColumns.Zip(relation.ParentColumns, (childColumn, parentColumn) => $"C.{Q(childColumn)} = P.{Q(parentColumn)}"));
            // SQL Server does not check an FK whose child key contains a NULL, hence the same rule here.
            var nonNull = string.Join(" AND ", relation.ChildColumns.Select(column => $"C.{Q(column)} IS NOT NULL"));
            var missing = $"P.{Q(relation.ParentColumns[0])} IS NULL";
            var sql = $"SELECT COUNT_BIG(*) FROM {child} C LEFT JOIN {parent} P ON {join} WHERE {nonNull} AND {missing};";
            await using var cmd = new SqlCommand(sql, c);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            if (count > 0)
                throw new InvalidOperationException($"Relationship preflight failed: {count:N0} source row(s) in {relation.ChildTable} reference missing row(s) in {relation.ParentTable} ({relation.ConstraintName}). Fix the source data or include valid parent records before syncing.");
            progress?.Report($"Validated {relation.ConstraintName}: {relation.ParentTable} → {relation.ChildTable}.");
        }
    }

    private static string ToQuotedTable(string key)
    {
        var parts = key.Split('.', 2);
        if (parts.Length != 2) throw new InvalidOperationException($"Invalid table key '{key}'.");
        return $"{Q(parts[0])}.{Q(parts[1])}";
    }

    private static async Task ValidateTargetParentsForChildAsync(string sourceCs, SqlConnection target, DataMoveTable childTable, IReadOnlyCollection<DataMoveRelation> relations)
    {
        var childKey = $"{childTable.Schema}.{childTable.Name}";
        var childRelations = relations.Where(r => string.Equals(r.ChildTable, childKey, StringComparison.OrdinalIgnoreCase));
        await using var source = new SqlConnection(sourceCs);
        await source.OpenAsync();
        foreach (var relation in childRelations)
        {
            // Build only a key projection from source. The parent lookup runs on the target,
            // so it verifies what will really be available to the imminent INSERT.
            var sourceChild = ToQuotedTable(relation.ChildTable);
            var columns = relation.ChildColumns.Select(Q).ToList();
            var nonNull = string.Join(" AND ", columns.Select(c => $"{c} IS NOT NULL"));
            var values = new List<object?[]>();
            await using (var read = new SqlCommand($"SELECT DISTINCT {string.Join(", ", columns)} FROM {sourceChild} WHERE {nonNull};", source))
            await using (var reader = await read.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                {
                    var row = new object?[reader.FieldCount];
                    reader.GetValues(row);
                    values.Add(row);
                }

            foreach (var valuesChunk in values.Chunk(500))
            {
                var clauses = new List<string>();
                await using var check = target.CreateCommand();
                for (var rowIndex = 0; rowIndex < valuesChunk.Length; rowIndex++)
                {
                    var comparisons = new List<string>();
                    for (var columnIndex = 0; columnIndex < relation.ParentColumns.Count; columnIndex++)
                    {
                        var parameter = $"@p{rowIndex}_{columnIndex}";
                        comparisons.Add($"P.{Q(relation.ParentColumns[columnIndex])} = {parameter}");
                        check.Parameters.AddWithValue(parameter, valuesChunk[rowIndex][columnIndex] ?? DBNull.Value);
                    }
                    clauses.Add($"(V.n = {rowIndex} AND {string.Join(" AND ", comparisons)})");
                }
                check.CommandText = $"SELECT COUNT_BIG(*) FROM (VALUES {string.Join(", ", valuesChunk.Select((_, i) => $"({i})"))}) AS V(n) WHERE NOT EXISTS (SELECT 1 FROM {ToQuotedTable(relation.ParentTable)} P WHERE {string.Join(" OR ", clauses)});";
                // The VALUES wrapper only supplies one row per requested key; each OR clause is parameterized.
                var missing = Convert.ToInt64(await check.ExecuteScalarAsync());
                if (missing > 0)
                    throw new InvalidOperationException($"Target relationship check failed before copying {relation.ChildTable}: {missing:N0} required {relation.ParentTable} key(s) are absent in the target ({relation.ConstraintName}). The parent table was included first but did not produce these keys; inspect its copy log and source data.");
            }
        }
    }
    private static List<DataMoveTable> GetDependencyOrder(List<DataMoveTable> tables, IReadOnlyDictionary<string,List<string>> parents)
    {
        var byKey=tables.ToDictionary(t=>$"{t.Schema}.{t.Name}",StringComparer.OrdinalIgnoreCase); var remaining=tables.ToDictionary(t=>$"{t.Schema}.{t.Name}",t=>new HashSet<string>(parents.TryGetValue($"{t.Schema}.{t.Name}",out var p)?p.Where(byKey.ContainsKey):[],StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase); var ordered=new List<DataMoveTable>(); while(remaining.Count>0){var ready=remaining.Where(x=>x.Value.Count==0).Select(x=>x.Key).OrderBy(x=>x).ToList(); if(ready.Count==0) throw new InvalidOperationException("Foreign-key cycle detected among selected tables: "+string.Join(", ",remaining.Keys)+". Remove the cycle or copy it manually."); foreach(var key in ready){ordered.Add(byKey[key]); remaining.Remove(key); foreach(var deps in remaining.Values) deps.Remove(key);}} return ordered;
    }
    private sealed class TableInfo { public TableInfo(string schema,string name){Schema=schema;Name=name;} public string Schema{get;} public string Name{get;} public List<(int Ordinal,string Name)> PrimaryKeysWithOrder=>PrimaryKeys; public List<(int Ordinal,string Name)> PrimaryKeys { get; }=[]; public List<string> WritableColumns {get;}=[]; public List<string> PrimaryKeysNames=>PrimaryKeys.Select(x=>x.Name).ToList(); }
}
