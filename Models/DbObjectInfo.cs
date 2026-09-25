using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SchemaCompare.Models;

public enum DbObjectType
{
    Table,
    View,
    StoredProcedure,
    Function,
    Trigger
}

public class DbObjectInfo
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = string.Empty;
    public DbObjectType ObjectType { get; set; }

    public string FullName => $"[{Schema}].[{Name}]";
    public string DisplayName => $"{Schema}.{Name}";

    public string TypeIcon => ObjectType switch
    {
        DbObjectType.Table           => "🗂",
        DbObjectType.View            => "👁",
        DbObjectType.StoredProcedure => "⚙",
        DbObjectType.Function        => "𝑓",
        DbObjectType.Trigger         => "⚡",
        _                            => "•"
    };

    public string TypeLabel => ObjectType switch
    {
        DbObjectType.Table           => "TABLE",
        DbObjectType.View            => "VIEW",
        DbObjectType.StoredProcedure => "PROCEDURE",
        DbObjectType.Function        => "FUNCTION",
        DbObjectType.Trigger         => "TRIGGER",
        _                            => "OBJECT"
    };

    public override string ToString() => DisplayName;
}
