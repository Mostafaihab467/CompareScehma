namespace SchemaCompare.Models;

/// <summary>
/// One edge of the dependency graph around a selected object. <see cref="Direction"/>
/// is read from the object's point of view: <see cref="UsesDirection"/> means the
/// selected object needs the other one, <see cref="UsedByDirection"/> means the other
/// one needs the selected object.
/// </summary>
public sealed record DependencyRow(string Direction, string ObjectName, string ObjectType, string Detail)
{
    public const string UsesDirection = "Uses";
    public const string UsedByDirection = "Used by";

    public bool IsUses => Direction == UsesDirection;

    /// <summary>sys type_desc in the words a DBA reads.</summary>
    public string Kind => FriendlyKind(ObjectType);

    /// <summary>Column / constraint name when the edge is narrower than the object.</summary>
    public string Note => Detail;

    public static string FriendlyKind(string typeDesc) => typeDesc switch
    {
        "USER_TABLE" => "Table",
        "VIEW" => "View",
        "SQL_STORED_PROCEDURE" or "PROCEDURE" => "Stored Procedure",
        "SQL_SCALAR_FUNCTION" => "Scalar Function",
        "SQL_TABLE_VALUED_FUNCTION" => "Table-Valued Function",
        "SQL_INLINE_TABLE_VALUED_FUNCTION" => "Inline Function",
        "SQL_TRIGGER" or "TRIGGER" => "Trigger",
        "FOREIGN_KEY" => "Foreign Key",
        "CHECK_CONSTRAINT" => "Check Constraint",
        "DEFAULT_CONSTRAINT" => "Default",
        "ASSEMBLY_CLUSTERED_INDEX" or "ASSEMBLY_SCALAR" => "Assembly",
        "USER_DEFINED_TABLE_TYPE" => "Table Type",
        "SEQUENCE_OBJECT" => "Sequence",
        "SYNONYM" => "Synonym",
        "EXTERNAL" => "External reference",
        _ => FriendlyFallback(typeDesc)
    };

    private static string FriendlyFallback(string typeDesc) =>
        string.IsNullOrEmpty(typeDesc) ? "Object" : typeDesc.Replace('_', ' ');
}

/// <summary>Everything the catalog says about one object's neighbours.</summary>
public sealed class ObjectDependencies
{
    public ObjectDependencies(string schema, string name, string typeDesc, List<DependencyRow> rows)
    {
        Schema = schema;
        Name = name;
        TypeDesc = typeDesc;
        Rows = rows;
        Uses = rows.Where(r => r.IsUses).ToList();
        UsedBy = rows.Where(r => !r.IsUses).ToList();
    }

    public string Schema { get; }
    public string Name { get; }
    public string TypeDesc { get; }
    public List<DependencyRow> Rows { get; }

    public string FullName => $"[{Schema}].[{Name}]";
    public string Kind => DependencyRow.FriendlyKind(TypeDesc);
    public bool HasAny => Rows.Count > 0;

    public List<DependencyRow> Uses { get; }
    public List<DependencyRow> UsedBy { get; }
    public bool HasUses => Uses.Count > 0;
    public bool HasUsedBy => UsedBy.Count > 0;

    public string SummaryText => HasAny
        ? $"Uses {Uses.Count} object(s) · used by {UsedBy.Count} object(s)"
        : $"No dependencies recorded for {FullName} — nothing in this database reads it and it reads nothing.";
}
