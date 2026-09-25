using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SchemaCompare.Services;

namespace SchemaCompare.Models;

/// <summary>
/// State for the visual Query Constructor. Lives only inside the dialog.
/// All collection types are observable so the preview text and "Apply" enablement
/// update automatically as the user edits dropdowns / checkboxes.
/// </summary>

public enum AggregateFunction
{
    None,
    Count,
    CountStar,
    CountBig,
    Sum,
    Avg,
    Min,
    Max,
    StDev,
    StDevP,
    Var,
    VarP
}

/// <summary>T-SQL name for an aggregate, e.g. CountBig → COUNT_BIG.</summary>
public static class AggregateFunctionExtensions
{
    public static string ToSql(this AggregateFunction a) => a switch
    {
        AggregateFunction.CountBig => "COUNT_BIG",
        AggregateFunction.StDev    => "STDEV",
        AggregateFunction.StDevP   => "STDEVP",
        AggregateFunction.Var      => "VAR",
        AggregateFunction.VarP     => "VARP",
        _                          => a.ToString().ToUpperInvariant()
    };
}

public enum FilterOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessOrEqual,
    GreaterThan,
    GreaterOrEqual,
    Like,
    NotLike,
    In,
    NotIn,
    IsNull,
    IsNotNull,
    Between
}

public sealed record FilterOperatorOption(FilterOperator Operator, string Label)
{
    public override string ToString() => Label;
}

public sealed record JoinTypeOption(JoinType JoinType, string Label)
{
    public override string ToString() => Label;
}

public static class FilterOperatorExtensions
{
    public static string ToFriendlyLabel(this FilterOperator op) => op switch
    {
        FilterOperator.Equal => "= (equals)",
        FilterOperator.NotEqual => "<> (does not equal)",
        FilterOperator.LessThan => "< (less than)",
        FilterOperator.LessOrEqual => "<= (less than or equal)",
        FilterOperator.GreaterThan => "> (greater than)",
        FilterOperator.GreaterOrEqual => ">= (greater than or equal)",
        FilterOperator.Like => "LIKE (contains pattern)",
        FilterOperator.NotLike => "NOT LIKE (does not match)",
        FilterOperator.In => "IN (list of values)",
        FilterOperator.NotIn => "NOT IN (none of list)",
        FilterOperator.IsNull => "IS NULL (empty / missing)",
        FilterOperator.IsNotNull => "IS NOT NULL (has value)",
        FilterOperator.Between => "BETWEEN (range: val1, val2)",
        _ => op.ToString()
    };
}

public enum FilterConjunction
{
    And,
    Or
}

public enum JoinType
{
    Inner,
    LeftOuter,
    RightOuter,
    FullOuter,
    Cross
}

public static class JoinTypeExtensions
{
    public static string ToFriendlyLabel(this JoinType jt) => jt switch
    {
        JoinType.Inner => "INNER JOIN (matching rows)",
        JoinType.LeftOuter => "LEFT JOIN (all from left + matches)",
        JoinType.RightOuter => "RIGHT JOIN (all from right + matches)",
        JoinType.FullOuter => "FULL JOIN (all rows both sides)",
        JoinType.Cross => "CROSS JOIN (every combination)",
        _ => jt.ToString()
    };
}

public enum SortDirection
{
    Asc,
    Desc
}

/// <summary>One selected output column (or "all columns").</summary>
public partial class SelectedColumn : ObservableObject
{
    /// <summary>Back-reference to the owning table — set by the view-model when the column is added.
    /// Lets the UI bind the column-name dropdown to <c>{Binding Table.Columns}</c> without fragile
    /// <c>$parent[...]</c> ancestor walking.</summary>
    public BuilderTable? Table { get; set; }

    [ObservableProperty] private string _tableAlias = string.Empty;     // alias declared on BuilderTable, or "" for *
    [ObservableProperty] private string _columnName = string.Empty;     // empty == "*"
    [ObservableProperty] private AggregateFunction _aggregate = AggregateFunction.None;
    [ObservableProperty] private string _alias = string.Empty;
}

/// <summary>A table participating in the FROM clause, with its own alias.</summary>
public partial class BuilderTable : ObservableObject
{
    [ObservableProperty] private string _schema = "dbo";
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private QuerySchemaService.TableInfo? _selectedTableInfo;

    /// <summary>Aliased identifier as it appears in SQL: [s].[t] [a].</summary>
    public string Qualified => $"[{Schema}].[{Name}]";
    public string AliasOrName => string.IsNullOrWhiteSpace(Alias) ? $"[{Name}]" : $"[{Alias}]";
    public string DisplayName => $"{Schema}.{Name}";

    /// <summary>Columns available for this table; populated by the view-model after the
    /// schema cache loads. Used to fill the "columns" and "filters" dropdowns.</summary>
    public ObservableCollection<string> Columns { get; } = new();
    public ObservableCollection<SelectedColumn> SelectedColumns { get; } = new();

    partial void OnSelectedTableInfoChanged(QuerySchemaService.TableInfo? value)
    {
        if (value == null) return;
        Schema = value.Schema;
        Name = value.Name;
        if (string.IsNullOrWhiteSpace(Alias))
            Alias = value.Name.Length > 0 ? value.Name[..1].ToLowerInvariant() : "t";
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Qualified));
    }

    partial void OnAliasChanged(string value)
    {
        OnPropertyChanged(nameof(AliasOrName));
        // Refresh any SelectedColumn that references this table so its alias stays in sync.
        foreach (var c in SelectedColumns)
            if (string.IsNullOrEmpty(c.TableAlias) || c.TableAlias == Alias)
                c.TableAlias = value;
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(Qualified));
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnSchemaChanged(string value)
    {
        OnPropertyChanged(nameof(Qualified));
        OnPropertyChanged(nameof(DisplayName));
    }
}

/// <summary>One JOIN clause joining a second (or later) table onto the FROM list.</summary>
public partial class JoinClause : ObservableObject
{
    [ObservableProperty] private JoinType _joinType = JoinType.Inner;
    [ObservableProperty] private BuilderTable _rightTable = new();
    [ObservableProperty] private string _leftColumn = string.Empty;   // "<alias>.<col>"
    [ObservableProperty] private string _rightColumn = string.Empty;  // "<alias>.<col>"
}

/// <summary>One WHERE predicate; conjunction with the previous one is AND/OR.</summary>
public partial class FilterCondition : ObservableObject
{
    [ObservableProperty] private FilterConjunction _conjunction = FilterConjunction.And;
    [ObservableProperty] private string _tableAlias = string.Empty;
    [ObservableProperty] private string _columnName = string.Empty;
    [ObservableProperty] private FilterOperator _operator = FilterOperator.Equal;

    /// <summary>For IN/NOT IN: comma-separated. For BETWEEN: "a,b". For everything else: literal.</summary>
    [ObservableProperty] private string _value = string.Empty;

    public bool NeedsValue => Operator != FilterOperator.IsNull && Operator != FilterOperator.IsNotNull;

    partial void OnOperatorChanged(FilterOperator value)
    {
        OnPropertyChanged(nameof(NeedsValue));
    }
}

/// <summary>One HAVING predicate over an aggregated group: e.g. COUNT(*) > 5.</summary>
public partial class HavingCondition : ObservableObject
{
    [ObservableProperty] private FilterConjunction _conjunction = FilterConjunction.And;
    [ObservableProperty] private AggregateFunction _aggregate = AggregateFunction.CountStar;
    [ObservableProperty] private string _tableAlias = string.Empty;
    [ObservableProperty] private string _columnName = string.Empty;
    [ObservableProperty] private FilterOperator _operator = FilterOperator.GreaterThan;

    [ObservableProperty] private string _value = string.Empty;

    public bool NeedsValue => Operator != FilterOperator.IsNull && Operator != FilterOperator.IsNotNull;

    /// <summary>False for COUNT(*) — no column picker needed.</summary>
    public bool NeedsColumn => Aggregate != AggregateFunction.CountStar;

    partial void OnOperatorChanged(FilterOperator value) => OnPropertyChanged(nameof(NeedsValue));
    partial void OnAggregateChanged(AggregateFunction value) => OnPropertyChanged(nameof(NeedsColumn));

    /// <summary>The aggregated left side as SQL: COUNT(*) or [t].[Col].</summary>
    public string AggregateSql => Aggregate == AggregateFunction.CountStar
        ? "COUNT(*)"
        : $"[{TableAlias}].[{ColumnName}]";
}

/// <summary>One ORDER BY entry.</summary>
public partial class OrderByItem : ObservableObject
{
    [ObservableProperty] private string _tableAlias = string.Empty;
    [ObservableProperty] private string _columnName = string.Empty;
    [ObservableProperty] private SortDirection _direction = SortDirection.Asc;
}

/// <summary>One GROUP BY entry (alias + column picked separately).</summary>
public partial class GroupByItem : ObservableObject
{
    [ObservableProperty] private string _tableAlias = string.Empty;
    [ObservableProperty] private string _columnName = string.Empty;

    public string Qualified => $"{TableAlias}.{ColumnName}";
}

/// <summary>Root model. Reset() restores a clean "pick a table" state.</summary>
public partial class QueryBuilderModel : ObservableObject
{
    public ObservableCollection<BuilderTable> Tables { get; } = new();
    public ObservableCollection<JoinClause>   Joins  { get; } = new();
    public ObservableCollection<FilterCondition> Filters { get; } = new();
    public ObservableCollection<HavingCondition> Having { get; } = new();
    public ObservableCollection<GroupByItem> GroupBy { get; } = new();
    public ObservableCollection<OrderByItem> OrderBy { get; } = new();

    [ObservableProperty] private bool _distinct;
    [ObservableProperty] private int  _topN;          // 0 == no TOP
    [ObservableProperty] private int  _fetchNext;     // 0 == no OFFSET/FETCH
    [ObservableProperty] private int  _offsetRows;

    [ObservableProperty] private string _generatedSql = "-- Pick at least one table to begin";

    public void Reset()
    {
        Tables.Clear();
        Joins.Clear();
        Filters.Clear();
        Having.Clear();
        GroupBy.Clear();
        OrderBy.Clear();
        Distinct = false;
        TopN = 0;
        FetchNext = 0;
        OffsetRows = 0;
        GeneratedSql = "-- Pick at least one table to begin";
    }
}