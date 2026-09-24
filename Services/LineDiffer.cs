namespace SchemaCompare.Services;

/// <summary>
/// Line-level LCS so schema compare can paint source-only lines green and
/// target-only lines red (the same visual language as SSMS / git diffs).
/// </summary>
public static class LineDiffer
{
    /// <summary>1-based line numbers in <paramref name="text"/> that are not
    /// matched in the LCS against <paramref name="partner"/>.</summary>
    public static HashSet<int> UniqueLines(string? text, string? partner)
    {
        var a = Split(text);
        var b = Split(partner);
        if (a.Length == 0) return [];
        if (b.Length == 0)
        {
            var all = new HashSet<int>(a.Length);
            for (var i = 1; i <= a.Length; i++) all.Add(i);
            return all;
        }

        if ((long)a.Length * b.Length > 1_200_000)
            return UniqueByCount(a, b);

        var n = a.Length;
        var m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        for (var j = m - 1; j >= 0; j--)
            dp[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                ? dp[i + 1, j + 1] + 1
                : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var matched = new bool[n];
        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                matched[x] = true;
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
                x++;
            else
                y++;
        }

        var unique = new HashSet<int>();
        for (var i = 0; i < n; i++)
            if (!matched[i]) unique.Add(i + 1);
        return unique;
    }

    private static HashSet<int> UniqueByCount(string[] a, string[] b)
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in b)
        {
            remaining.TryGetValue(line, out var c);
            remaining[line] = c + 1;
        }
        var unique = new HashSet<int>();
        for (var i = 0; i < a.Length; i++)
        {
            if (remaining.TryGetValue(a[i], out var c) && c > 0)
                remaining[a[i]] = c - 1;
            else
                unique.Add(i + 1);
        }
        return unique;
    }

    private static string[] Split(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToArray();
    }
}
