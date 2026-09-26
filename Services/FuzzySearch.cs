namespace SchemaCompare.Services;

/// <summary>
/// Subsequence matcher for the command palette: ranks a short typed query against long
/// dotted names ("dbo.Pre_Users") and prose ("Format document"), and refuses anything
/// whose characters do not all appear in order.
/// </summary>
public static class FuzzySearch
{
    /// <summary>Sentinel for "does not match at all"; every real score is far above it.</summary>
    public const int NoMatch = int.MinValue;

    /// <summary>A run that breaks somewhere other than a word start costs this much.</summary>
    private const int BreakPenalty = 6;

    private const int LengthDivisor = 8;

    /// <summary>Score for one field; higher is better, <see cref="NoMatch"/> when the query is not a subsequence.</summary>
    public static int Score(string? text, string? query)
    {
        if (string.IsNullOrEmpty(text)) return NoMatch;
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0) return 0;

        var t = text!;
        var memo = new int[q.Length, t.Length + 1];
        for (var row = 0; row < q.Length; row++)
            for (var col = 0; col <= t.Length; col++)
                memo[row, col] = Uncomputed;

        var best = Rank(t, q, 0, -1, memo);
        return best == NoMatch ? NoMatch : best - t.Length / LengthDivisor;
    }

    private const int Uncomputed = int.MaxValue;

    /// <summary>
    /// Best score for <c>query[qi..]</c> given the last matched character sat at <c>prev</c>
    /// (-1 before the first one). The greedy leftmost walk would take a bad first hit and
    /// pay for it forever, so every viable hit is tried and the best kept.
    /// </summary>
    private static int Rank(string t, string q, int qi, int prev, int[,] memo)
    {
        if (qi == q.Length) return 0;
        if (memo[qi, prev + 1] != Uncomputed) return memo[qi, prev + 1];

        var needle = char.ToLowerInvariant(q[qi]);
        var best = NoMatch;
        for (var i = prev + 1; i < t.Length; i++)
        {
            if (char.ToLowerInvariant(t[i]) != needle) continue;
            var rest = qi + 1 == q.Length ? 0 : Rank(t, q, qi + 1, i, memo);
            if (rest == NoMatch) continue;
            var total = Step(t, i, prev) + rest;
            if (total > best) best = total;
        }

        memo[qi, prev + 1] = best;
        return best;
    }

    private static int Step(string t, int hit, int prev)
    {
        if (hit == prev + 1) return 1;
        // A new run. Landing on the first letter of a word is what typing an acronym does, so it
        // costs nothing; breaking anywhere else is the operator reading past letters they typed.
        var opensWord = hit == 0 || IsBoundary(t[hit - 1]) || IsCamelHump(t, hit);
        return 1 - (opensWord ? 0 : BreakPenalty);
    }

    private static bool IsBoundary(char c) => c is '.' or '_' or '-' or ' ' or '[' or '/';

    /// <summary>The capital that opens a word inside a camel-hump name — "L" in "CustomerList".</summary>
    private static bool IsCamelHump(string t, int index) =>
        index > 0 && char.IsUpper(t[index]) && !char.IsUpper(t[index - 1]) && !IsBoundary(t[index - 1]);

    /// <summary>
    /// Items whose score clears, best first. Callers that match on more than one field pass a
    /// scorer and return <see cref="NoMatch"/> to drop an item. Ties keep the input order, so a
    /// catalog sorted by schema and name stays predictable instead of shuffling per keystroke.
    /// </summary>
    public static IReadOnlyList<T> Order<T>(IEnumerable<T> items, Func<T, int> score)
    {
        var scored = new List<(T Item, int Score, int Position)>();
        var position = 0;
        foreach (var item in items)
        {
            var s = score(item);
            if (s != NoMatch) scored.Add((item, s, position++));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Position)
            .Select(x => x.Item)
            .ToList();
    }
}
