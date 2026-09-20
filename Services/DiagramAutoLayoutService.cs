using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Automatic layered layout: parent tables on top, children below.
/// Foreign-key depth is computed with cycle guards so circular references
/// degrade to same-layer placement instead of crashing.
/// </summary>
public static class DiagramAutoLayoutService
{
    private const double ColumnGap = 60;
    private const double RowGap = 90;
    private const double StartX = 60;
    private const double StartY = 60;
    private const double WrapWidth = 3200;

    public static void ApplyLayout(
        IEnumerable<DiagramTableNode> tables,
        IEnumerable<DiagramRelation> relations)
    {
        var list = tables.ToList();
        if (list.Count == 0) return;

        var keys = new HashSet<string>(list.Select(t => t.FullName), StringComparer.OrdinalIgnoreCase);
        var parents = relations
            .GroupBy(r => r.ChildTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.ParentTable).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int GetDepth(string key, HashSet<string> stack)
        {
            if (depth.TryGetValue(key, out var cached)) return cached;
            if (!stack.Add(key)) return 0; // cycle — stop descending
            var d = 0;
            if (parents.TryGetValue(key, out var ps))
            {
                foreach (var p in ps)
                {
                    if (!keys.Contains(p)) continue;
                    if (string.Equals(p, key, StringComparison.OrdinalIgnoreCase)) continue; // self-reference
                    d = Math.Max(d, GetDepth(p, stack) + 1);
                }
            }
            stack.Remove(key);
            depth[key] = d;
            return d;
        }

        foreach (var t in list)
            GetDepth(t.FullName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // Widest layers wrap into sub-rows so very wide schemas stay navigable.
        var rows = new List<List<DiagramTableNode>>();
        foreach (var layer in list
                     .GroupBy(t => depth.GetValueOrDefault(t.FullName, 0))
                     .OrderBy(g => g.Key))
        {
            var ordered = layer.OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase).ToList();
            var current = new List<DiagramTableNode>();
            var currentWidth = 0.0;
            foreach (var node in ordered)
            {
                if (current.Count > 0 && currentWidth + DiagramTableNode.CardWidth > WrapWidth)
                {
                    rows.Add(current);
                    current = [];
                    currentWidth = 0;
                }
                current.Add(node);
                currentWidth += DiagramTableNode.CardWidth + ColumnGap;
            }
            if (current.Count > 0) rows.Add(current);
        }

        var y = StartY;
        foreach (var row in rows)
        {
            var x = StartX;
            foreach (var node in row)
            {
                node.X = x;
                node.Y = y;
                x += node.NodeWidth + ColumnGap;
            }
            y += row.Max(n => n.NodeHeight) + RowGap;
        }
    }

    /// <summary>
    /// Organizes the active table and its directly-connected tables into a clean,
    /// symmetrical flow:
    /// - Referenced parent tables on the left (arranged vertically)
    /// - Active table in the center
    /// - Referencing child tables on the right (arranged vertically)
    /// This ensures zero crisscrossing and immediate visual clarity.
    /// </summary>
    public static void OrganizeCluster(
        DiagramTableNode activeNode,
        IReadOnlyList<DiagramTableNode> relatedNodes,
        IReadOnlyList<DiagramRelation> relations)
    {
        if (relatedNodes.Count == 0) return;

        var relatedMap = relatedNodes.ToDictionary(n => n.FullName, StringComparer.OrdinalIgnoreCase);
        var parentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var childKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in relations)
        {
            if (r.ChildTable.Equals(activeNode.FullName, StringComparison.OrdinalIgnoreCase))
            {
                if (relatedMap.ContainsKey(r.ParentTable))
                    parentKeys.Add(r.ParentTable);
            }
            else if (r.ParentTable.Equals(activeNode.FullName, StringComparison.OrdinalIgnoreCase))
            {
                if (relatedMap.ContainsKey(r.ChildTable))
                    childKeys.Add(r.ChildTable);
            }
        }

        var parents = parentKeys.Select(k => relatedMap[k]).OrderBy(n => n.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        var children = childKeys.Select(k => relatedMap[k]).OrderBy(n => n.FullName, StringComparer.OrdinalIgnoreCase).ToList();

        var others = relatedNodes.Where(n => !parentKeys.Contains(n.FullName) && !childKeys.Contains(n.FullName)).ToList();
        if (others.Count > 0)
            children.AddRange(others);

        const double HorizGap = 120;
        const double VertGap = 35;

        var anchorCenterX = Math.Max(DiagramTableNode.CardWidth + HorizGap + 50, activeNode.X);
        var anchorCenterY = Math.Max(160, activeNode.Y);

        activeNode.X = anchorCenterX;
        activeNode.Y = anchorCenterY;

        if (parents.Count > 0)
        {
            var totalParentHeight = parents.Sum(p => p.NodeHeight) + (parents.Count - 1) * VertGap;
            var startY = anchorCenterY + activeNode.NodeHeight / 2 - totalParentHeight / 2;
            var parentX = Math.Max(40, anchorCenterX - DiagramTableNode.CardWidth - HorizGap);

            var currY = Math.Max(40, startY);
            foreach (var p in parents)
            {
                p.X = parentX;
                p.Y = currY;
                currY += p.NodeHeight + VertGap;
            }
        }

        if (children.Count > 0)
        {
            var totalChildHeight = children.Sum(c => c.NodeHeight) + (children.Count - 1) * VertGap;
            var startY = anchorCenterY + activeNode.NodeHeight / 2 - totalChildHeight / 2;
            var childX = anchorCenterX + activeNode.NodeWidth + HorizGap;

            var currY = Math.Max(40, startY);
            foreach (var c in children)
            {
                c.X = childX;
                c.Y = currY;
                currY += c.NodeHeight + VertGap;
            }
        }
    }
}
