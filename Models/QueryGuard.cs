namespace SchemaCompare.Models;

public enum GuardLevel
{
    /// <summary>Worth reading before you press F5; it does not stop the script.</summary>
    Advisory,
    /// <summary>Removes or rewrites data the operator may not have meant to touch.</summary>
    Dangerous,
}

/// <summary>One thing a script will destroy. <paramref name="Rule"/> is a stable id so a test,
/// a log line and a support conversation can name the same finding without quoting prose.</summary>
public readonly record struct GuardFinding(GuardLevel Level, string Rule, string Message);

/// <summary>What a script does to data, read off the text before the server ever sees it.</summary>
public sealed class GuardReport
{
    public GuardReport(IReadOnlyList<GuardFinding> findings) => Findings = findings;

    public IReadOnlyList<GuardFinding> Findings { get; }

    public bool IsClear => Findings.Count == 0;

    public bool RequiresConfirmation => Findings.Any(f => f.Level == GuardLevel.Dangerous);

    /// <summary>The worst thing in the script, for a dialog title.</summary>
    public string Headline
    {
        get
        {
            foreach (var finding in Findings)
                if (finding.Level == GuardLevel.Dangerous) return finding.Message;
            return Findings.Count > 0 ? Findings[0].Message : "No destructive statements found.";
        }
    }

    /// <summary>One line for the status bar: the first finding and how many more there were.</summary>
    public string Summary => IsClear
        ? ""
        : Findings.Count == 1
            ? Findings[0].Message
            : $"{Findings[0].Message} (+{Findings.Count - 1} more)";
}
