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

    public string RowBackground => IsSelected ? "#E8DEF8" : "Transparent";
    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(RowBackground));

    public string StatusColor => Status switch
    {
        DiffStatus.Added   => "#27AE60",
        DiffStatus.Changed => "#F39C12",
        DiffStatus.Deleted => "#E74C3C",
        _ => "#95A5A6"
    };
}
