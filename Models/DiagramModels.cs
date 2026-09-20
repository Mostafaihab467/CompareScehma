using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

/// <summary>One column shown inside a diagram table card.</summary>
public sealed class DiagramColumn
{
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsForeignKey { get; set; }
    public bool IsNullable { get; init; }
    public bool IsIdentity { get; init; }

    public string Icon => IsPrimaryKey ? "🔑" : IsForeignKey ? "🔗" : "•";
    public string DisplayType => IsIdentity ? $"{DataType} ⭡" : IsNullable ? $"{DataType} ?" : DataType;
}

/// <summary>A draggable table node on the diagram canvas.</summary>
public sealed partial class DiagramTableNode : ObservableObject
{
    public const int MaxVisibleColumns = 15;
    public const double CardWidth = 240;

    public required string Schema { get; init; }
    public required string Name { get; init; }
    public List<DiagramColumn> Columns { get; init; } = [];
    public long RowCount { get; init; }

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isDimmed;
    [ObservableProperty] private bool _isIsolatedTarget;
    [ObservableProperty] private int _relationCount;
    [ObservableProperty] private string _relationsTooltipText = string.Empty;
    /// <summary>Set by relation isolation: hides unrelated tables from the canvas
    /// without touching <see cref="IsVisible"/> (checkboxes) or saved layouts.</summary>
    [ObservableProperty] private bool _isHiddenByIsolation;

    public bool HasRelations => RelationCount > 0;
    public string RelationBadge => $"🔗 {RelationCount}";

    partial void OnRelationCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasRelations));
        OnPropertyChanged(nameof(RelationBadge));
    }

    public string FullName => $"{Schema}.{Name}";
    public string DisplayName => $"[{Schema}].[{Name}]";
    public string HeaderSubtitle => $"{Columns.Count} col(s)" + (RowCount > 0 ? $" • {RowCount:N0} rows" : "");

    public double NodeWidth => CardWidth;
    public double NodeHeight => 60 + Math.Min(Columns.Count, MaxVisibleColumns) * 22 + (HasOverflow ? 26 : 10);

    public IReadOnlyList<DiagramColumn> VisibleColumns => Columns.Take(MaxVisibleColumns).ToList();
    public bool HasOverflow => Columns.Count > MaxVisibleColumns;
    public string OverflowText => HasOverflow ? $"+{Columns.Count - MaxVisibleColumns} more column(s)…" : string.Empty;
}

/// <summary>A foreign-key edge drawn between two table nodes.</summary>
public sealed class DiagramRelation
{
    public required string ConstraintName { get; init; }
    public required string ChildTable { get; init; }
    public required string ParentTable { get; init; }
    public required List<string> ChildColumns { get; init; }
    public required List<string> ParentColumns { get; init; }

    public string Label => $"{ChildTable} → {ParentTable} ({ConstraintName}: {string.Join(", ", ChildColumns)})";
}

/// <summary>Display model for a single foreign-key relation of an active table.</summary>
public sealed class DiagramRelationDisplayItem
{
    public required string ConstraintName { get; init; }
    public required string ChildTable { get; init; }
    public required string ParentTable { get; init; }
    public required List<string> ChildColumns { get; init; }
    public required List<string> ParentColumns { get; init; }
    public required bool IsOutgoing { get; init; }
    public required string OtherTable { get; init; }

    public string DirectionBadge => IsOutgoing ? "↗ References" : "↙ Referenced by";
    public string DirectionColor => IsOutgoing ? "#38BDF8" : "#A78BFA";
    public string MappingText => $"{string.Join(", ", ChildColumns)} → {string.Join(", ", ParentColumns)}";
    public string FullSummary => IsOutgoing
        ? $"↗ References {OtherTable}\nConstraint: {ConstraintName}\nMapping: {MappingText}"
        : $"↙ Referenced by {OtherTable}\nConstraint: {ConstraintName}\nMapping: {MappingText}";
}

/// <summary>Persisted diagram layout: node positions, visibility and zoom.</summary>
public sealed class DiagramPersistedState
{
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public DateTime SavedAtUtc { get; set; }
    public double Zoom { get; set; } = 1.0;
    public List<DiagramTableState> Tables { get; set; } = [];
}

public sealed class DiagramTableState
{
    public string FullName { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsVisible { get; set; } = true;
}
