using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.Views;

/// <summary>One row of the file-relocation grid.</summary>
public partial class RestoreFileRow : ObservableObject
{
    public string LogicalName { get; init; } = "";
    public string TypeText { get; init; } = "";
    public string SizeText { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string RelocatedPath { get; set; } = "";

    [ObservableProperty] private string _destinationPath = "";
}

/// <summary>Everything the dialog needs that only the service layer can know.</summary>
public sealed class RestoreDraft
{
    public required string BackupPath { get; init; }
    public required string ConnectedDatabase { get; init; }
    public List<BackupSetInfo> Sets { get; init; } = [];
    public string DataDirectory { get; init; } = "";
    public string LogDirectory { get; init; } = "";
    public bool TargetExists { get; init; }
    public required Func<int, Task<List<BackupFileInfo>>> LoadFiles { get; init; }
}

public partial class RestoreDatabaseDialog : Window
{
    private readonly RestoreDraft _draft;
    private readonly ObservableCollection<RestoreFileRow> _rows = [];
    private bool _loadingFiles;

    private RestoreDatabaseDialog(RestoreDraft draft)
    {
        InitializeComponent();
        _draft = draft;

        SourceText.Text = $"Backup file: {draft.BackupPath}";
        TargetBox.Text = draft.Sets.FirstOrDefault()?.DatabaseName ?? draft.ConnectedDatabase;

        foreach (var set in draft.Sets) SetBox.Items.Add(set.ListLabel);
        SetBox.SelectedIndex = Math.Max(0, draft.Sets.Count - 1);

        FilesGrid.ItemsSource = _rows;

        SetBox.SelectionChanged += async (_, _) => await LoadFilesAsync();
        TargetBox.TextChanged += (_, _) => { ReSuggestPaths(); RefreshState(); };
        RelocateBox.PropertyChanged += (_, _) => { ApplyRelocation(RelocateBox.IsChecked == true); RefreshState(); };
        StopAtBox.PropertyChanged += (_, _) => { StopAtText.IsEnabled = StopAtBox.IsChecked == true; RefreshState(); };
        StopAtText.TextChanged += (_, _) => RefreshState();
        StopAtText.TextChanged += (_, _) => RefreshState();
        ReplaceBox.PropertyChanged += (_, _) => RefreshState();
        NoRecoveryBox.PropertyChanged += (_, _) => RefreshState();

        CancelBtn.Click += (_, _) => Close();
        OkBtn.Click += (_, _) => Close(BuildPlan());

        Loaded += async (_, _) => await LoadFilesAsync();
    }

    /// <summary>The chosen restore, or null when the user cancelled.</summary>
    public static Task<RestorePlan?> ShowAsync(Window owner, RestoreDraft draft) =>
        new RestoreDatabaseDialog(draft).ShowDialog<RestorePlan?>(owner);

    private BackupSetInfo? SelectedSet =>
        SetBox.SelectedIndex >= 0 && SetBox.SelectedIndex < _draft.Sets.Count
            ? _draft.Sets[SetBox.SelectedIndex]
            : null;

    private async Task LoadFilesAsync()
    {
        var set = SelectedSet;
        if (set == null) return;

        _loadingFiles = true;
        try
        {
            var files = await _draft.LoadFiles(set.Position);
            _rows.Clear();
            foreach (var file in files)
            {
                var relocated = RelocateTo(file, TargetBox.Text ?? "");
                _rows.Add(new RestoreFileRow
                {
                    LogicalName = file.LogicalName,
                    TypeText = file.TypeText,
                    SizeText = file.SizeText,
                    SourcePath = file.PhysicalName,
                    RelocatedPath = relocated,
                    DestinationPath = RelocateBox.IsChecked == true ? relocated : file.PhysicalName
                });
            }
        }
        finally
        {
            _loadingFiles = false;
        }
        RefreshState();
    }

    private string RelocateTo(BackupFileInfo file, string targetDatabase)
    {
        var directory = file.IsLog
            ? (_draft.LogDirectory.Length > 0 ? _draft.LogDirectory : _draft.DataDirectory)
            : _draft.DataDirectory;
        return directory.Length == 0 || string.IsNullOrWhiteSpace(targetDatabase)
            ? file.PhysicalName
            : ManagerScriptBuilder.SuggestedPath(file, directory, targetDatabase);
    }

    /// <summary>
    /// Renaming the target must rename the files it lands as: two databases cannot
    /// share one physical file, so a copy left with the source's file names would
    /// collide with the source the moment it lives in the same folder.
    /// </summary>
    private void ReSuggestPaths()
    {
        if (_loadingFiles) return;
        foreach (var row in _rows)
        {
            var file = new BackupFileInfo
            {
                LogicalName = row.LogicalName,
                PhysicalName = row.SourcePath,
                Type = row.TypeText == "Log" ? 'L' : 'D'
            };
            row.RelocatedPath = RelocateTo(file, TargetBox.Text ?? "");
            if (RelocateBox.IsChecked == true) row.DestinationPath = row.RelocatedPath;
        }
    }

    private void ApplyRelocation(bool relocate)
    {
        if (_loadingFiles) return;
        foreach (var row in _rows)
            row.DestinationPath = relocate ? row.RelocatedPath : row.SourcePath;
    }

    private bool IsInPlace =>
        string.Equals((TargetBox.Text ?? "").Trim(), SelectedSet?.DatabaseName, StringComparison.OrdinalIgnoreCase);

    private void RefreshState()
    {
        if (_loadingFiles) return;

        ReplaceBox.IsEnabled = _draft.TargetExists;
        var inPlace = IsInPlace;
        if (!inPlace) ReplaceBox.IsChecked = false;

        var target = (TargetBox.Text ?? "").Trim();
        var messages = new List<string>();
        if (target.Length == 0)
            messages.Add("Enter the database to restore.");
        if (inPlace && _draft.TargetExists && ReplaceBox.IsChecked != true)
            messages.Add($"{target} already exists — tick \"Overwrite the existing database\" to replace it, " +
                         "or change the name above to restore a copy alongside it.");
        if (inPlace && RelocateBox.IsChecked == true)
            messages.Add("An in-place restore that also relocates leaves the old files of " + target +
                         " on disk untouched — only do this to move the database to new storage.");
        if (_draft.DataDirectory.Length == 0 && RelocateBox.IsChecked == true)
            messages.Add("This instance did not report default data folders; destinations were left as the backup's own paths.");
        if (StopAtBox.IsChecked == true && !DateTime.TryParse(StopAtText.Text, out _))
            messages.Add("STOPAT needs a date like 2026-09-25 14:30:00.");
        if (StopAtBox.IsChecked == true && NoRecoveryBox.IsChecked == true)
            messages.Add("STOPAT with \"leave restoring\" holds the database offline until a follow-up restore recovers it.");

        WarningText.Text = string.Join("\n", messages);
        WarningText.IsVisible = messages.Count > 0;

        var plan = TryBuildPlan(out _);
        PreviewBox.Text = plan == null ? "-- complete the choices above to see the exact RESTORE statement" : plan;
        OkBtn.IsEnabled = messages.Count == 0 && plan != null;
        PlanSummary.Text = _rows.Count == 0
            ? "No files read from this set."
            : $"{_rows.Count} file(s), {_rows.Count(r => r.DestinationPath != r.SourcePath)} relocated.";
    }

    private string? TryBuildPlan(out RestorePlan? built)
    {
        built = null;
        var set = SelectedSet;
        if (set == null || _rows.Count == 0) return null;

        var plan = new RestorePlan
        {
            BackupPath = _draft.BackupPath,
            SetPosition = set.Position,
            TargetDatabase = (TargetBox.Text ?? "").Trim(),
            SourceDatabase = set.DatabaseName,
            ReplaceExisting = ReplaceBox.IsChecked == true,
            NoRecovery = NoRecoveryBox.IsChecked == true
        };
        if (StopAtBox.IsChecked == true && DateTime.TryParse(StopAtText.Text, out var stopAt))
            plan.StopAt = stopAt;

        foreach (var row in _rows)
        {
            plan.Files.Add(new BackupFileInfo
            {
                LogicalName = row.LogicalName,
                PhysicalName = row.SourcePath,
                Type = row.TypeText == "Log" ? 'L' : 'D'
            });
            if (row.DestinationPath != row.SourcePath)
                plan.Moves[row.LogicalName] = row.DestinationPath;
        }

        try
        {
            built = plan;
            return ManagerScriptBuilder.RestoreDatabase(plan);
        }
        catch (InvalidOperationException)
        {
            built = null;
            return null;
        }
    }

    private RestorePlan? BuildPlan()
    {
        TryBuildPlan(out var plan);
        return plan;
    }
}
