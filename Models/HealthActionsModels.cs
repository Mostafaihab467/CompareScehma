using System.Text.RegularExpressions;
using Avalonia.Media;

namespace SchemaCompare.Models;

/// <summary>One SQL Server error log record, read through xp_readerrorlog.</summary>
public sealed class HealthErrorLogRow
{
    public DateTime LogDate { get; init; }
    public string ProcessInfo { get; init; } = "";
    public string Text { get; init; } = "";

    /// <summary>Severity parsed out of the message text; xp_readerrorlog has no severity column.</summary>
    public int? Severity => TrySeverity(Text) is { } s ? s : null;

    public int? ErrorNumber => TryErrorNumber(Text);

    private static readonly Regex SeverityRegex =
        new(@"Severity:\s*(\d{1,2})", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    private static readonly Regex ErrorNumberRegex =
        new(@"Error:\s*(\d{3,6}),", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    private static int? TrySeverity(string text)
    {
        var match = SeverityRegex.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var s) ? s : null;
    }

    private static int? TryErrorNumber(string text)
    {
        var match = ErrorNumberRegex.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : null;
    }

    /// <summary>Severity 11 and up is what an operator actually scans for.</summary>
    public bool IsError => Severity >= 11;

    public string TimeText => LogDate == DateTime.MinValue ? "" : LogDate.ToString("yyyy-MM-dd HH:mm:ss");

    public string SeverityText => Severity?.ToString() ?? "";

    /// <summary>Red tint on severity 11+ records — the ones an operator has to read.</summary>
    public IBrush RowBackground => IsError
        ? new SolidColorBrush(Color.Parse("#59803030"))
        : Brushes.Transparent;
}

/// <summary>One hop in a blocking chain, oldest blocker first.</summary>
public sealed class BlockingChainLink
{
    public int SessionId { get; init; }
    public int BlockedBy { get; init; }
    public string Database { get; init; } = "";
    public string Login { get; init; } = "";
    public string Host { get; init; } = "";
    public string Program { get; init; } = "";
    public string Status { get; init; } = "";
    public string WaitType { get; init; } = "";
    public long WaitMs { get; init; }

    /// <summary>The statement this session was running when it became a blocker.</summary>
    public string Statement { get; init; } = "";

    public bool IsHeadBlocker { get; init; }
}
