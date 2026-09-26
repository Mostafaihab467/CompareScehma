using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.ViewModels;

/// <summary>
/// The list behind the command palette: every row the catalog and the query window's own
/// commands offered, re-ranked on each keystroke. One row is chosen and the window is done —
/// that is why accepting is the only verb here, and why a row with no host to take it is
/// refused instead of half-applied.
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IReadOnlyList<PaletteItem> _all;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private PaletteItem? _selected;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public CommandPaletteViewModel(IReadOnlyList<PaletteItem> items)
    {
        _all = items;
        Rebuild();
    }

    public ObservableCollection<PaletteItem> Results { get; } = [];

    /// <summary>Set by the host: what an accepted row does. With no host, nothing does.</summary>
    public Func<PaletteItem, Task>? Accepted { get; set; }

    public int MatchCount => Results.Count;

    partial void OnSearchTextChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var query = SearchText;
        var ranked = FuzzySearch.Order(_all, item => item.Score(query));
        Results.Clear();
        foreach (var item in ranked) Results.Add(item);
        Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(MatchCount));
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        var item = Selected;
        if (item == null)
        {
            StatusMessage = "Nothing matches that yet.";
            return;
        }

        if (Accepted == null)
        {
            StatusMessage = $"{item.Title}: nothing here can take the choice — nothing was inserted.";
            return;
        }

        await Accepted(item);
    }
}
