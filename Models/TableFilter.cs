using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

public partial class TableFilter : ObservableObject
{
    [ObservableProperty] private string _columnName = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsNoValue))]
    private string _operator = "=";
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private string _logicalOp = "AND";  // AND | OR

    public static readonly string[] Operators =
    [
        "=", "!=", "<>", ">", "<", ">=", "<=",
        "LIKE", "NOT LIKE", "IS NULL", "IS NOT NULL",
        "IN", "NOT IN", "BETWEEN"
    ];

    public static readonly string[] LogicalOps = ["AND", "OR"];

    /// <summary>True for operators that do not need a value (IS NULL / IS NOT NULL).</summary>
    public bool NeedsNoValue =>
        Operator is "IS NULL" or "IS NOT NULL";
}
