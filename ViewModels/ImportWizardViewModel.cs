using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.ViewModels;

/// <summary>
/// View-model of the import wizard: a file, a destination database and a table, and the
/// column-by-column mapping between them. Reading and previewing touch nothing but the
/// file; only Load writes, and only after the host dialog has confirmed the script.
/// </summary>
public partial class ImportWizardViewModel : ObservableObject
{
    private readonly CsvImportService _service = new();
    private readonly SavedConnectionsService _savedService = new();
    private bool _applyingProfile;

    public ObservableCollection<SavedConnection> SavedConnections { get; } = [];
    public ObservableCollection<ImportColumn> Columns { get; } = [];

    /// <summary>Delimiter dropdown contents (label + character).</summary>
    public IReadOnlyList<string> DelimiterOptions { get; } =
        CsvImportService.Delimiters.Select(d => d.Label).ToList();

    /// <summary>Type dropdown contents for a mapped column.</summary>
    public IReadOnlyList<string> TypeOptions { get; } = CsvImportService.SqlTypes;

    /// <summary>Authentication dropdown contents (Windows, SQL, Entra ID flows).</summary>
    public IReadOnlyList<AuthMethod> AuthMethods { get; } = AuthMethod.All;

    [ObservableProperty] private double _uiScale = AppSettings.DefaultUiScale;

    [ObservableProperty] private string _server = "";
    [ObservableProperty] private string _database = "";
    [ObservableProperty] private AuthMethod _auth = AuthMethod.Windows;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private SavedConnection? _selectedSaved;

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _schema = "dbo";
    [ObservableProperty] private string _tableName = "";
    [ObservableProperty] private bool _createNewTable = true;
    [ObservableProperty] private bool _hasHeaderRow = true;
    [ObservableProperty] private int _delimiterIndex;
    [ObservableProperty] private ImportColumn? _selectedColumn;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _statusMessage = "Pick a file, then check what the wizard guessed.";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _scriptText = "";
    [ObservableProperty] private bool _hasColumns;
    [ObservableProperty] private bool _hasScript;

    public ICommand ReadFileCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand MatchTargetCommand { get; }
    public ICommand LoadCommand { get; }

    /// <summary>Set by the view: opens the file picker and returns the chosen path or null.</summary>
    public Func<Task<string?>>? PickOpenFileAsync { get; set; }

    /// <summary>Set by the view: (title, warning, script) → did the user approve it.</summary>
    public Func<string, string, string, Task<bool>>? ShowScriptConfirmAsync { get; set; }

    public ImportWizardViewModel()
    {
        foreach (var saved in _savedService.Load()) SavedConnections.Add(saved);
        try { UiScale = new AppSettingsService().Load().UiScale; }
        catch { /* the default scale is fine */ }
        ReadFileCommand = new AsyncRelayCommand(ReadFileAsync, () => !IsBusy);
        PreviewCommand = new RelayCommand(BuildPreview, () => HasColumns);
        MatchTargetCommand = new AsyncRelayCommand(MatchTargetAsync, () => HasColumns && !IsBusy);
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => HasColumns && !IsBusy);
    }

    /// <summary>Prefills the destination from the connection the launcher already holds.</summary>
    public void InitializeFrom(ConnectionInfo target)
    {
        _applyingProfile = true;
        Server = target.Server; Database = target.Database;
        Auth = AuthMethod.For(target.Authentication, target.UseWindowsAuth);
        Username = target.Username; Password = target.Password;
        SelectedSaved = SavedConnections.FirstOrDefault(p =>
            p.Matches(target.Server, target.Database, target.UseWindowsAuth, target.Username,
                target.Authentication));
        _applyingProfile = false;
    }

    private char Delimiter => CsvImportService.Delimiters[
        Math.Clamp(DelimiterIndex, 0, CsvImportService.Delimiters.Length - 1)].Char;

    private ConnectionInfo BuildTarget() => new()
    {
        Server = Server, Database = Database,
        UseWindowsAuth = Auth.IsWindows, Authentication = Auth.AuthenticationMethod,
        Username = Username, Password = Password
    };

    partial void OnSelectedSavedChanged(SavedConnection? value)
    {
        if (_applyingProfile || value == null) return;
        _applyingProfile = true;
        Server = value.Server; Database = value.Database;
        Auth = value.Auth; Username = value.Username; Password = value.Password;
        _applyingProfile = false;
    }

    partial void OnCreateNewTableChanged(bool value)
    {
        if (HasColumns) BuildPreview();
    }

    partial void OnTableNameChanged(string value)
    {
        if (HasColumns) BuildPreview();
    }

    private bool MissingInput()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        { return Fail("Choose the file to import."); }
        if (string.IsNullOrWhiteSpace(Server)) return Fail("Enter the server to import into.");
        if (string.IsNullOrWhiteSpace(Database)) return Fail("Enter the database to import into.");
        if (string.IsNullOrWhiteSpace(TableName.Trim())) return Fail("Name the table.");
        if (!Columns.Any(c => c.Include)) return Fail("Keep at least one column ticked.");
        return false;
    }

    private bool MissingFile()
    {
        if (string.IsNullOrWhiteSpace(FilePath)) { Fail("Choose the file to import."); return true; }
        if (!File.Exists(FilePath)) { Fail($"The file {FilePath} is not there."); return true; }
        return false;
    }

    private bool Fail(string message)
    {
        ErrorMessage = message; ShowError = true; StatusMessage = message;
        return false;
    }

    private async Task ReadFileAsync()
    {
        if (PickOpenFileAsync != null)
        {
            var picked = await PickOpenFileAsync();
            if (string.IsNullOrWhiteSpace(picked)) return;
            FilePath = picked;
        }

        if (MissingFile()) return;
        IsBusy = true; ShowError = false;
        try
        {
            var table = await Task.Run(() => CsvImportService.ReadTable(FilePath, Delimiter, HasHeaderRow));
            Columns.Clear();
            for (var i = 0; i < table.Headers.Count; i++)
            {
                var samples = table.Sample.Select(r => i < r.Length ? r[i] : null).ToList();
                var (type, nullable, note) = CsvImportService.InferType(samples);
                var name = CsvImportService.SanitizeName(table.Headers[i]);
                Columns.Add(new ImportColumn
                {
                    SourceName = table.Headers[i], Position = i, TargetName = name,
                    SqlType = type, Nullable = nullable, Note = note,
                    IsKey = i == 0 && LooksLikeKey(name, samples)
                });
            }

            HasColumns = Columns.Count > 0;
            if (string.IsNullOrWhiteSpace(TableName))
                TableName = CsvImportService.SanitizeName(Path.GetFileNameWithoutExtension(FilePath));
            BuildPreview();
            SummaryText = $"{Columns.Count} column(s), {table.Sample.Count:N0} sample row(s) read." +
                          (table.Sample.Count == CsvImportService.SampleRows ? " Types are inferred from the sample only." : "");
            StatusMessage = "Check the types before loading — a guess is only a guess.";
            AppLog.Info($"Import read {Columns.Count} column(s) from {Path.GetFileName(FilePath)}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message; ShowError = true; StatusMessage = "Could not read the file.";
            AppLog.Error("Import", ex, $"reading {FilePath} failed");
        }
        finally { IsBusy = false; }
    }

    /// <summary>A first column named like an id whose sample is whole and unique.</summary>
    private static bool LooksLikeKey(string name, IReadOnlyList<string?> samples)
    {
        if (!name.EndsWith("id", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("Id", StringComparison.OrdinalIgnoreCase)) return false;
        var values = samples.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
        return values.Count > 0 && values.Count == values.Distinct(StringComparer.Ordinal).Count() &&
               values.All(v => long.TryParse(v, out _));
    }

    /// <summary>Rebuild the script pane from the mapping as it stands now.</summary>
    private void BuildPreview()
    {
        if (!HasColumns) { ScriptText = ""; HasScript = false; return; }
        var included = Columns.Where(c => c.Include).ToList();
        ScriptText = CreateNewTable
            ? $"-- Create {Schema.Trim()}.{TableName.Trim()} and load {Path.GetFileName(FilePath)} into it\n" +
              CsvImportService.BuildCreateTable(Schema.Trim(), TableName.Trim(), included,
                  included.Where(c => c.IsKey).Select(c => c.TargetName).ToList()) +
              $"\nGO\n\n-- Then bulk-copy every row of the file into it ({included.Count} column(s)).\n" +
              "-- Nothing is written until the load is confirmed.\n"
            : $"-- Load {Path.GetFileName(FilePath)} into the existing {Schema.Trim()}.{TableName.Trim()}\n" +
              $"-- Columns used: {string.Join(", ", included.Select(c => c.TargetName))}\n" +
              "-- The table is not created, altered or emptied by this wizard.\n";
        HasScript = true;
    }

    /// <summary>
    /// Read the destination table's real columns and line the mapping up with them: types
    /// come from the server, a column the table lacks is flagged rather than created.
    /// </summary>
    private async Task MatchTargetAsync()
    {
        if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(Database))
        { Fail("Enter the server and database holding the table."); return; }
        if (string.IsNullOrWhiteSpace(TableName.Trim())) { Fail("Name the table."); return; }

        IsBusy = true; ShowError = false;
        try
        {
            var target = BuildTarget();
            var actual = await CsvImportService.ReadTargetColumnsAsync(target.ConnectionString,
                Schema.Trim(), TableName.Trim());
            if (actual.Count == 0)
            {
                CreateNewTable = true;
                Fail($"{Schema.Trim()}.{TableName.Trim()} does not exist in {target.Database} — the wizard will create it.");
                return;
            }

            var matched = 0;
            foreach (var column in Columns)
            {
                var onServer = actual.FirstOrDefault(a =>
                    a.Name.Equals(column.TargetName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (onServer == null)
                {
                    column.MissingOnTarget = true;
                    column.Include = false;
                    column.Note = "not in the target table — unticked";
                    continue;
                }

                column.MissingOnTarget = false;
                column.Include = true;
                column.SqlType = onServer.Type;
                column.Nullable = onServer.Nullable;
                column.Note = onServer.IsComputed ? "computed — cannot be loaded"
                    : onServer.IsIdentity ? "identity — values are kept" : "matched to the table";
                matched++;
            }

            BuildPreview();
            var skipped = Columns.Count - matched;
            SummaryText = $"{matched} of {Columns.Count} column(s) exist in {Schema.Trim()}.{TableName.Trim()}" +
                          (skipped > 0 ? $"; {skipped} unticked." : ".");
            StatusMessage = skipped == 0
                ? "Every column matched — the load can go ahead."
                : "Some columns were unticked; the load will not touch them.";
            AppLog.Info($"Import matched {matched}/{Columns.Count} column(s) against " +
                        $"{Schema.Trim()}.{TableName.Trim()} on {target.SafeForLog}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message; ShowError = true; StatusMessage = "Could not read the target table.";
            AppLog.Error("Import", ex, "matching the target table failed");
        }
        finally { IsBusy = false; }
    }

    private async Task LoadAsync()
    {
        if (MissingInput()) return;
        var included = Columns.Where(c => c.Include).ToList();
        if (included.Any(c => c.MissingOnTarget && !CreateNewTable))
        { Fail("A ticked column is not in the target table. Untick it or create a new table."); return; }
        if (included.Any(c => string.IsNullOrWhiteSpace(c.TargetName)))
        { Fail("Every ticked column needs a target name."); return; }

        var target = BuildTarget();
        var keys = included.Where(c => c.IsKey).Select(c => c.TargetName).ToList();
        var script = CsvImportService.BuildCreateTable(Schema.Trim(), TableName.Trim(), included, keys);
        var warning = CreateNewTable
            ? $"Creates {Schema.Trim()}.{TableName.Trim()} if it is not there and writes every row of " +
              $"{Path.GetFileName(FilePath)} into it. An existing table is left as it is."
            : $"Writes every row of {Path.GetFileName(FilePath)} into the existing " +
              $"{Schema.Trim()}.{TableName.Trim()}. The table is not emptied, altered or created.";

        // Fails closed: with no host dialog wired, nothing is written.
        if (ShowScriptConfirmAsync == null || !await ShowScriptConfirmAsync("Import data", warning, script))
        {
            StatusMessage = "The load was not confirmed — nothing was written.";
            return;
        }

        IsBusy = true; ShowError = false;
        var progress = new Progress<string>(text => StatusMessage = text);
        try
        {
            var result = await _service.LoadAsync(target, Schema.Trim(), TableName.Trim(), included,
                FilePath, Delimiter, HasHeaderRow, CreateNewTable, keys, progress);
            SummaryText = $"{result.RowsLoaded:N0} row(s) loaded into {Schema.Trim()}.{TableName.Trim()}" +
                          (result.TableCreated ? " (table created by this run)." : ".");
            StatusMessage = SummaryText;
            AppLog.Info($"Import loaded {result.RowsLoaded} row(s) into {Schema.Trim()}.{TableName.Trim()} " +
                        $"on {target.SafeForLog}, created={result.TableCreated}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message; ShowError = true;
            StatusMessage = "The load was rolled back — the table holds what it held before.";
            AppLog.Error("Import", ex, $"loading {Schema.Trim()}.{TableName.Trim()} failed");
        }
        finally { IsBusy = false; }
    }
}
