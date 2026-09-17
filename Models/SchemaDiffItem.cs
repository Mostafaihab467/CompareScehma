using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

public enum DiffStatus { Added, Changed, Deleted }

public partial class SchemaDiffItem : ObservableObject
{
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public DiffStatus Status { get; set; }
    public string SourceScript { get; set; } = string.Empty;
    public string TargetScript { get; set; } = string.Empty;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isIncluded = true;

    public string RowBackground => IsSelected ? "#2E2547" : "Transparent";
    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(RowBackground));

    public string StatusColor => Status switch
    {
        DiffStatus.Added   => "#34D399",
        DiffStatus.Changed => "#FBBF24",
        DiffStatus.Deleted => "#F87171",
        _ => "#8E8EA3"
    };
}
