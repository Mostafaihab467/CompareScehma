using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

/// <summary>
/// One expandable group in the differences tree (e.g. Tables, Procedures).
/// The arrow/expander binds to <see cref="IsExpanded"/> to hide/show its items.
/// </summary>
public partial class DiffGroup : ObservableObject
{
    public string ObjectType { get; set; } = string.Empty;
    public string Icon { get; set; } = "📦";
    public ObservableCollection<SchemaDiffItem> Items { get; } = [];

    [ObservableProperty] private bool _isExpanded = true;

    public int Count => Items.Count;
    public int AddedCount => Items.Count(i => i.Status == DiffStatus.Added);
    public int ChangedCount => Items.Count(i => i.Status == DiffStatus.Changed);
    public int DeletedCount => Items.Count(i => i.Status == DiffStatus.Deleted);

    public string Title => $"{Icon}  {ObjectType}  ({Count})";
    public string Summary => $"+{AddedCount}  ~{ChangedCount}  -{DeletedCount}";

    public void RefreshCounts()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(AddedCount));
        OnPropertyChanged(nameof(ChangedCount));
        OnPropertyChanged(nameof(DeletedCount));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
    }

    public static string IconFor(string objectType)
    {
        var t = (objectType ?? string.Empty).ToLowerInvariant();
        if (t.Contains("table")) return "🗂";
        if (t.Contains("procedure") || t == "p" || t.Contains("proc")) return "⚙";
        if (t.Contains("view")) return "👁";
        if (t.Contains("function")) return "ƒ";
        if (t.Contains("trigger")) return "⚡";
        if (t.Contains("index")) return "🔢";
        if (t.Contains("key") || t.Contains("constraint") || t.Contains("foreign") || t.Contains("primary") || t.Contains("check") || t.Contains("default")) return "🔗";
        if (t.Contains("schema")) return "📁";
        if (t.Contains("user") || t.Contains("role") || t.Contains("login")) return "👤";
        if (t.Contains("type")) return "🧩";
        if (t.Contains("sequence")) return "🔢";
        return "📦";
    }

    public static string NormalizeType(string? objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType)) return "Other";
        var t = objectType.Trim();
        // DacFx sometimes reports short or plural names — unify for stable grouping.
        var lower = t.ToLowerInvariant();
        if (lower is "p" or "sqlstoredprocedure") return "Stored Procedures";
        if (lower is "v" or "sqlview") return "Views";
        if (lower is "u" or "sqltable") return "Tables";
        if (lower is "fn" or "if" or "tf" or "sqlscalarfunction" or "sqlinlinefunction" or "sqltablefunction") return "Functions";
        if (lower is "tr" or "sqltrigger" or "sqldmltrigger" or "sqldatabase trigger") return "Triggers";
        if (lower.Contains("stored") && lower.Contains("procedure")) return "Stored Procedures";
        if (lower.Contains("function")) return "Functions";
        if (lower.Contains("trigger")) return "Triggers";
        if (lower is "tables" or "table") return "Tables";
        if (lower is "views" or "view") return "Views";
        if (lower is "procedures" or "procedure") return "Stored Procedures";
        if (lower is "functions") return "Functions";
        if (lower is "triggers") return "Triggers";
        if (lower is "indexes" or "index") return "Indexes";
        return t;
    }
}
