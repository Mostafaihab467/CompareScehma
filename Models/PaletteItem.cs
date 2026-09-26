using System.Windows.Input;
using SchemaCompare.Services;

namespace SchemaCompare.Models;

/// <summary>What a palette row stands for. The group decides which of the two actions is filled.</summary>
public enum PaletteGroup
{
    Command,
    Table,
    View,
    StoredProcedure
}

/// <summary>
/// One row of the command palette: either an app command to run, or a database object to
/// write a read script for. Building a row never touches the server; only accepting one does,
/// and the script it produces is inserted as text rather than executed.
/// </summary>
public sealed class PaletteItem
{
    /// <summary>A prose match ("Format document") ranks below a name match, but still matches.</summary>
    public const int DetailPenalty = 6;

    public required string Title { get; init; }

    /// <summary>Group plus what accepting the row does, shown greyed after the title.</summary>
    public required string Detail { get; init; }

    public required PaletteGroup Group { get; init; }

    /// <summary>Set for command rows: the same <see cref="ICommand"/> the toolbar button runs.</summary>
    public ICommand? Command { get; init; }

    /// <summary>Set for object rows: the script to insert, built from live metadata on accept.</summary>
    public Func<CancellationToken, Task<string>>? BuildScriptAsync { get; init; }

    public bool IsCommand => Command != null;

    public int Score(string? query)
    {
        var title = FuzzySearch.Score(Title, query);
        var detail = FuzzySearch.Score(Detail, query);
        if (detail != FuzzySearch.NoMatch) detail -= DetailPenalty;
        return title == FuzzySearch.NoMatch ? detail : Math.Max(title, detail);
    }

    public override string ToString() => $"{Title} — {Detail}";
}
