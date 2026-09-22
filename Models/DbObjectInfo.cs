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

/// <summary>
/// Grouped category node shown in the object tree sidebar.
/// Supports expanding/collapsing and category-specific search filtering.
/// </summary>
public partial class DbObjectGroup : ObservableObject
{
    public string GroupName { get; set; } = string.Empty;
    public DbObjectType ObjectType { get; set; }
    public string Icon { get; set; } = string.Empty;

    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private string _filterText = string.Empty;

    public List<DbObjectInfo> AllItems { get; set; } = [];
    public ObservableCollection<DbObjectInfo> FilteredItems { get; } = [];

    public int TotalCount => AllItems.Count;
    public int DisplayCount => FilteredItems.Count;

    public string Header => string.IsNullOrWhiteSpace(FilterText)
        ? $"{GroupName} ({TotalCount})"
        : $"{GroupName} ({DisplayCount}/{TotalCount})";

    public string ExpandIcon => IsExpanded ? "▼" : "▶";

    [RelayCommand]
    public void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
        OnPropertyChanged(nameof(ExpandIcon));
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandIcon));
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    public void SetItems(IEnumerable<DbObjectInfo> items)
    {
        AllItems = items.ToList();
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        FilteredItems.Clear();
        var q = FilterText?.Trim() ?? string.Empty;
        var matching = string.IsNullOrEmpty(q)
            ? AllItems
            : AllItems.Where(i => i.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                  i.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var item in matching)
            FilteredItems.Add(item);

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(DisplayCount));
        OnPropertyChanged(nameof(Header));
    }
}
