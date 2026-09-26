using Avalonia.Controls;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.Views;

/// <summary>Defaults the backup dialog starts from, all read from the server.</summary>
public sealed class BackupDraft
{
    public required string Database { get; init; }
    public required string SuggestedFileName { get; init; }

    /// <summary>Empty when the instance does not report a default backup folder.</summary>
    public string DefaultBackupDirectory { get; init; } = "";

    /// <summary>True when the recovery model allows log backups at all.</summary>
    public bool CanBackUpLog { get; init; } = true;
}

public partial class BackupDatabaseDialog : Window
{
    private readonly BackupDraft _draft;

    private BackupDatabaseDialog(BackupDraft draft)
    {
        InitializeComponent();
        _draft = draft;

        NoticeText.Text = "SQL Server writes this file from its own service account, so the path must exist on " +
                          "the database server — not on this PC. Use the restore dialog on that server to read it back.";

        var directory = draft.DefaultBackupDirectory.TrimEnd('\\', '/');
        PathBox.Text = directory.Length == 0
            ? draft.SuggestedFileName
            : $@"{directory}\{draft.SuggestedFileName}";

        if (!draft.CanBackUpLog)
        {
            ((ComboBoxItem)TypeBox.Items[1]!).IsEnabled = false;
            NoticeText.Text += " This database is in SIMPLE recovery, so only a full backup is available.";
        }

        foreach (var control in new Avalonia.Controls.Control[]
                 { TypeBox, PathBox, DescriptionBox, CompressBox, ChecksumBox, VerifyBox, CopyOnlyBox, OverwriteBox })
            control.PropertyChanged += (_, _) => RefreshPreview();

        CancelBtn.Click += (_, _) => Close();
        OkBtn.Click += (_, _) => Close(BuildRequest());
        RefreshPreview();
    }

    /// <summary>The chosen backup, or null when the user cancelled.</summary>
    public static Task<BackupRequest?> ShowAsync(Window owner, BackupDraft draft) =>
        new BackupDatabaseDialog(draft).ShowDialog<BackupRequest?>(owner);

    private BackupRequest? BuildRequest()
    {
        var path = PathBox.Text?.Trim() ?? "";
        if (path.Length > 0 && !System.IO.Path.HasExtension(path))
            path += TypeBox.SelectedIndex == 1 ? ".trn" : ".bak";

        var request = new BackupRequest
        {
            Database = _draft.Database,
            FilePath = path,
            LogBackup = TypeBox.SelectedIndex == 1,
            CopyOnly = CopyOnlyBox.IsChecked == true,
            Checksum = ChecksumBox.IsChecked == true,
            Compress = CompressBox.IsChecked == true,
            OverwriteMedia = OverwriteBox.IsChecked == true,
            VerifyAfterBackup = VerifyBox.IsChecked == true,
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim()
        };
        try
        {
            request.Validate();
            ErrorText.IsVisible = false;
            return request;
        }
        catch (InvalidOperationException ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.IsVisible = true;
            return null;
        }
    }

    private void RefreshPreview()
    {
        var request = BuildRequest();
        if (request == null)
        {
            PreviewBox.Text = "";
            OkBtn.IsEnabled = false;
            return;
        }

        OkBtn.IsEnabled = true;
        var script = ManagerScriptBuilder.BackupDatabase(request);
        if (request.VerifyAfterBackup)
            script += "\n\n" + ManagerScriptBuilder.VerifyBackup(request.FilePath, request.Checksum);
        PreviewBox.Text = script;
    }
}
