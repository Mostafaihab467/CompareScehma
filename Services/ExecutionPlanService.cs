using System.Globalization;
using System.Text;
using System.Xml.Linq;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Parses ShowPlanXML documents (captured via SET STATISTICS XML ON) into an
/// <see cref="ExecutionPlan"/> tree of operators. Pure XML → model, no DB access.
/// </summary>
public static class ExecutionPlanService
{
    /// <summary>True when a result table holds ShowPlanXML (single XML-typed column).</summary>
    public static bool IsPlanTable(QueryResultTable table) =>
        table.Columns.Count == 1 &&
        table.Columns[0].Contains("Showplan", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extracts the XML text of every plan row in a plan result table.</summary>
    public static IEnumerable<string> ExtractPlanXmls(QueryResultTable table)
    {
        foreach (var row in table.Rows)
        {
            var xml = table.Columns.Count > 0 &&
                      row.TryGetValue(table.Columns[0], out var v)
                ? v as string
                : null;
            if (!string.IsNullOrWhiteSpace(xml))
                yield return xml;
        }
    }

    /// <summary>Parses one or more ShowPlanXML documents into statements + operator trees.</summary>
    public static ExecutionPlan Parse(IEnumerable<string> planXmls)
    {
        var plan = new ExecutionPlan();
        foreach (var xml in planXmls)
        {
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch { continue; } // tolerate malformed fragments, keep the rest

            foreach (var stmt in doc.Descendants().Where(e => e.Name.LocalName == "StmtSimple"))
            {
                var statement = new PlanStatement
                {
                    StatementType = (string?)stmt.Attribute("StatementType") ?? "Statement",
                    StatementText = Truncate(((string?)stmt.Attribute("StatementText") ?? "").Trim()),
                    OptimizationLevel = (string?)stmt.Attribute("StatementOptmLevel"),
                    SubtreeCost = ParseDouble(stmt.Attribute("StatementSubTreeCost")?.Value)
                };
                var queryPlan = stmt.Elements().FirstOrDefault(e => e.Name.LocalName == "QueryPlan");
                if (queryPlan != null)
                {
                    statement.DegreeOfParallelism = ParseInt(queryPlan.Attribute("DegreeOfParallelism")?.Value);
                    statement.MemoryGrantKb = ParseInt(queryPlan.Attribute("MemoryGrant")?.Value);
                    statement.CachedPlanSizeKb = ParseInt(queryPlan.Attribute("CachedPlanSize")?.Value);
                }
                var rootRelOp = queryPlan?.Elements().FirstOrDefault(e => e.Name.LocalName == "RelOp");
                if (rootRelOp != null)
                    statement.Root = BuildNode(rootRelOp);
                statement.MissingIndex = ParseMissingIndex(stmt);
                plan.Statements.Add(statement);
            }
        }
        // Cost percentages are shares of the plan's most expensive statement.
        var total = plan.TotalCost;
        if (total > 0)
        {
            foreach (var node in plan.Statements.Where(s => s.Root != null)
                         .SelectMany(s => s.Root!.SelfAndDescendants()))
                node.CostPercent = node.SubtreeCost / total * 100.0;
        }
        return plan;
    }

    private static PlanNode BuildNode(XElement relOp)
    {
        var node = new PlanNode
        {
            PhysicalOp = (string?)relOp.Attribute("PhysicalOp") ?? "",
            LogicalOp = (string?)relOp.Attribute("LogicalOp") ?? "",
            IsParallel = ((string?)relOp.Attribute("Parallel")) == "1",
            EstimatedRows = ParseDouble(relOp.Attribute("EstimateRows")?.Value),
            SubtreeCost = ParseDouble(relOp.Attribute("EstimatedTotalSubtreeCost")?.Value),
            EstimatedIo = ParseDouble(relOp.Attribute("EstimateIO")?.Value),
            EstimatedCpu = ParseDouble(relOp.Attribute("EstimateCPU")?.Value),
            EstimatedRowSize = ParseDouble(relOp.Attribute("EstimateRowSize")?.Value),
            ObjectName = ExtractObjectName(relOp)
        };

        node.Warnings.AddRange(ParseWarnings(relOp));
        node.Predicate = ExtractPredicate(relOp);
        node.SortOrder = ExtractSortOrder(relOp);

        // The physical-op wrapper element (NestedLoops, IndexScan, Hash>Build/Probe, …)
        // sits directly under RelOp; child RelOps may be nested a level or two deeper.
        foreach (var child in CollectChildRelOps(relOp))
            node.Children.Add(BuildNode(child));

        node.NodeCost = Math.Max(0, node.SubtreeCost - node.Children.Sum(c => c.SubtreeCost));
        return node;
    }

    private static IEnumerable<string> ParseWarnings(XElement relOp)
    {
        var warnings = relOp.Elements().FirstOrDefault(e => e.Name.LocalName == "Warnings");
        if (warnings == null)
            yield break;
        foreach (var attr in warnings.Attributes())
        {
            if (attr.Value is "1" or "true")
                yield return attr.Name.LocalName;
        }
        foreach (var w in warnings.Elements())
        {
            var detail = string.Join(", ", w.Attributes()
                .Where(a => a.Name.LocalName is "Expression" or "SpillLevel" or "Column" or "Statement" or "ConvertIssue")
                .Select(a => $"{a.Name.LocalName}={a.Value}"));
            yield return detail.Length > 0 ? $"{w.Name.LocalName} ({detail})" : w.Name.LocalName;
        }
    }

    /// <summary>Descendants of an element, but never entering child RelOp subtrees.</summary>
    private static IEnumerable<XElement> LocalDescendants(XElement element)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "RelOp")
                continue;
            yield return child;
            foreach (var deeper in LocalDescendants(child))
                yield return deeper;
        }
    }

    /// <summary>Column names of the seek predicate or filter condition, for scan/seek/filter nodes.</summary>
    private static string? ExtractPredicate(XElement relOp)
    {
        var wrapper = relOp.Elements().FirstOrDefault(e => e.Name.LocalName != "OutputList" && e.Name.LocalName != "Warnings");
        if (wrapper == null) return null;
        var scope = LocalDescendants(wrapper).ToList();
        var seek = scope.FirstOrDefault(e => e.Name.LocalName == "SeekPredicates");
        if (seek != null)
        {
            var cols = seek.Descendants().Where(e => e.Name.LocalName == "ColumnReference")
                .Select(c => $"[{c.Attribute("Column")?.Value}]")
                .Distinct()
                .ToList();
            if (cols.Count > 0)
                return "Seek on " + string.Join(", ", cols);
        }
        var predicate = scope.FirstOrDefault(e => e.Name.LocalName == "Predicate");
        if (predicate != null)
        {
            var cols = predicate.Descendants().Where(e => e.Name.LocalName == "ColumnReference")
                .Select(c => $"[{c.Attribute("Column")?.Value}]")
                .Distinct().ToList();
            if (cols.Count > 0)
                return "Filter on " + string.Join(", ", cols);
        }
        return null;
    }

    private static string? ExtractSortOrder(XElement relOp)
    {
        var sort = relOp.Elements().FirstOrDefault(e => e.Name.LocalName == "Sort");
        if (sort == null) return null;
        var items = sort.Descendants().Where(e => e.Name.LocalName == "OrderByColumn")
            .Select(c =>
            {
                var name = c.Descendants().FirstOrDefault(e => e.Name.LocalName == "ColumnReference")?
                    .Attribute("Column")?.Value;
                var asc = (string?)c.Attribute("Ascending") != "0";
                return $"{name} {(asc ? "ASC" : "DESC")}";
            })
            .ToList();
        return items.Count == 0 ? null : string.Join(", ", items);
    }

    private static MissingIndexSuggestion? ParseMissingIndex(XElement stmt)
    {
        var group = stmt.Descendants().FirstOrDefault(e => e.Name.LocalName == "MissingIndexGroup");
        var missing = group?.Elements().FirstOrDefault(e => e.Name.LocalName == "MissingIndex");
        if (group == null || missing == null)
            return null;

        var db = (string?)missing.Attribute("Database") ?? "";
        var schema = (string?)missing.Attribute("Schema") ?? "";
        var table = (string?)missing.Attribute("Table") ?? "";
        var keys = new List<string>();
        var includes = new List<string>();
        foreach (var cg in missing.Elements().Where(e => e.Name.LocalName == "ColumnGroup"))
        {
            var usage = (string?)cg.Attribute("Usage");
            var target = usage == "INCLUDE" ? includes : keys;
            foreach (var col in cg.Elements().Where(e => e.Name.LocalName == "Column"))
                target.Add((string?)col.Attribute("Name") ?? "");
        }

        var indexName = $"IX_{table}_{DateTime.Now:HHmmss}";
        var script = $"CREATE NONCLUSTERED INDEX [{indexName}]\r\n    ON {db}.{schema}.{table} ({string.Join(", ", keys.Select(Q))})"
                     + (includes.Count > 0 ? $"\r\n    INCLUDE ({string.Join(", ", includes.Select(Q))})" : "")
                     + $"\r\n-- Estimated improvement: {ParseDouble(group.Attribute("Impact")?.Value):0.#}%";
        return new MissingIndexSuggestion
        {
            Impact = ParseDouble(group.Attribute("Impact")?.Value),
            Table = $"{db}.{schema}.{table}",
            KeyColumns = keys,
            IncludedColumns = includes,
            CreateScript = script
        };
    }

    private static string Q(string ident) => "[" + ident.Replace("]", "]]") + "]";

    private static IEnumerable<XElement> CollectChildRelOps(XElement relOp)
    {
        foreach (var child in relOp.Elements())
        {
            if (child.Name.LocalName == "OutputList")
                continue;
            foreach (var found in Collect(child))
                yield return found;
        }
    }

    private static IEnumerable<XElement> Collect(XElement element)
    {
        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "RelOp":
                    yield return child;
                    break;
                case "OutputList":
                case "DefinedValues":
                case "Warnings":
                    break;
                default:
                    foreach (var deeper in Collect(child))
                        yield return deeper;
                    break;
            }
        }
    }

    /// <summary>The first table/index reference under the operator, e.g. "[sys].[all_objects] — [idx]".
    /// Attribute values already include brackets, so they are used verbatim.</summary>
    private static string? ExtractObjectName(XElement relOp)
    {
        var obj = relOp.Descendants().FirstOrDefault(e => e.Name.LocalName == "Object");
        var schema = (string?)obj?.Attribute("Schema");
        var table = (string?)obj?.Attribute("Table");
        if (string.IsNullOrWhiteSpace(table))
            return null;
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(schema))
            sb.Append(schema).Append('.');
        sb.Append(table);
        var index = (string?)obj?.Attribute("Index");
        if (!string.IsNullOrWhiteSpace(index))
            sb.Append(" — ").Append(index);
        return sb.ToString();
    }

    private static double ParseDouble(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static int? ParseInt(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string Truncate(string text) =>
        text.Length <= 120 ? text : text[..117] + "…";
}
