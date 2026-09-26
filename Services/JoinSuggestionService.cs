using System.Text.RegularExpressions;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Writes the <c>ON</c> clause a join is about to need, from the foreign-key metadata the editor
/// already holds. Pure on purpose: it reads a pair of tables and a list of keys and never connects,
/// so the pairing rules can be asserted without a server and reused by anything that has the same
/// metadata — the query constructor included.
/// </summary>
public static class JoinSuggestionService
{
    /// <summary>An identifier that can be written bare in a script.</summary>
    private static readonly Regex PlainIdentifier = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Every <c>ON</c> clause that legitimately relates <paramref name="right"/> to
    /// <paramref name="left"/>.
    /// </summary>
    /// <remarks>
    /// Both directions of a key count, so <c>FROM Pre_Users u JOIN [Order] o</c> is offered the
    /// same clause as the other way round. A pair related by two keys yields two offers rather
    /// than one arbitrary pick: the two keys shape the result set differently, and choosing
    /// between them is the operator's decision, not the editor's. A key over several columns
    /// yields one clause that names all of them, because half a composite key joins on the wrong
    /// rows without saying so.
    /// </remarks>
    public static IReadOnlyList<JoinSuggestion> Between(
        JoinSide left, JoinSide right, IReadOnlyList<ForeignKeyRef> foreignKeys)
    {
        var suggestions = new List<JoinSuggestion>();
        var groups = foreignKeys
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrEmpty(g.Key));

        foreach (var group in groups)
        {
            var parts = group.OrderBy(f => f.Ordinal).ToList();
            var forward = Links(parts[0], left.Key, right.Key);
            var backward = !forward && Links(parts[0], right.Key, left.Key);
            if (!forward && !backward) continue;

            var pairs = new List<string>(parts.Count);
            var froms = new List<string>(parts.Count);
            var tos = new List<string>(parts.Count);
            foreach (var part in parts)
            {
                // The child column is always the one that points at the other table, whichever
                // side of the join the operator happened to write first.
                var child = forward ? part.FromColumn : part.ToColumn;
                var parent = forward ? part.ToColumn : part.FromColumn;
                pairs.Add($"{Qualified(left.Ref, child)} = {Qualified(right.Ref, parent)}");
                froms.Add(part.FromColumn);
                tos.Add(part.ToColumn);
            }

            suggestions.Add(new JoinSuggestion(
                "ON " + string.Join(" AND ", pairs),
                $"{group.Key}: {ShortName(parts[0].FromTable)}.{Join(froms)} → " +
                $"{ShortName(parts[0].ToTable)}.{Join(tos)}"));
        }

        return suggestions
            .OrderBy(s => s.Description, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>True when this key runs from <paramref name="fromKey"/> to
    /// <paramref name="toKey"/>, compared as case-insensitive, schema-qualified table keys.</summary>
    private static bool Links(ForeignKeyRef key, string fromKey, string toKey) =>
        string.Equals(key.FromKey, fromKey, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(key.ToKey, toKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>A column reference for a table the script names <paramref name="tableRef"/>,
    /// bracketed when the name cannot be written bare.</summary>
    private static string Qualified(string tableRef, string column) =>
        $"{tableRef}.{Quote(column)}";

    private static string Quote(string identifier) =>
        PlainIdentifier.IsMatch(identifier) ? identifier : $"[{identifier}]";

    /// <summary>Drops the schema: a tooltip reads better as <c>Order.UserId</c> than
    /// <c>dbo.Order.UserId</c>, and the key's own name already says which database object it is.</summary>
    private static string ShortName(string tableName)
    {
        var dot = tableName.LastIndexOf('.');
        return dot >= 0 ? tableName[(dot + 1)..] : tableName;
    }

    private static string Join(IReadOnlyList<string> columns) => string.Join(", ", columns);
}
