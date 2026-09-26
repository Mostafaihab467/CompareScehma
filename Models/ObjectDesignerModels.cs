namespace SchemaCompare.Models;

/// <summary>What the object designer is being asked to build.</summary>
public enum DesignerKind
{
    Table,
    View,
    StoredProcedure
}

/// <summary>One column of a table under construction.</summary>
public sealed class DesignerColumn
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "int";
    public bool Nullable { get; set; } = true;
    public bool IsKey { get; set; }
    public bool IsIdentity { get; set; }
    public string Default { get; set; } = "";
}

/// <summary>
/// The designer's answer: an object definition plus what to do with it. The script itself
/// is built by <see cref="Services.ManagerScriptBuilder.CreateObject" /> so the same text
/// is previewed, confirmed and executed.
/// </summary>
public sealed class DesignerSpec
{
    public required DesignerKind Kind { get; init; }
    public required string Schema { get; init; }
    public required string Name { get; init; }

    /// <summary>Columns of a table; empty for a view or procedure.</summary>
    public required IReadOnlyList<DesignerColumn> Columns { get; init; }

    /// <summary>View SELECT or procedure body; unused for a table.</summary>
    public required string Body { get; init; }

    /// <summary>True when the operator pressed Execute rather than Script.</summary>
    public bool ExecuteNow { get; init; }
}
