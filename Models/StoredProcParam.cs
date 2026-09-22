using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

public partial class StoredProcParam : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsOutput { get; set; }
    public string? DefaultValue { get; set; }

    /// <summary>Value the user entered for execution — bound to the Execute-tab grid.</summary>
    [ObservableProperty] private string _inputValue = string.Empty;

    public string DirectionLabel => IsOutput ? "OUTPUT" : "INPUT";
}
