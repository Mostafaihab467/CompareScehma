using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchemaCompare.Models;
using SchemaCompare.Services;

using SchemaCompare.Views;

namespace SchemaCompare.ViewModels;

public enum ManagerTab { Data, Structure, Definition, Execute }

public partial class DbManagerViewModel : ObservableObject
{
    private readonly DbManagerService _service = new();
    private readonly DatabaseBackupService _backupService = new();
    private readonly SavedConnectionsService _savedService = new();

    private CancellationTokenSource? _cts;

    // -------------------------------------------------------------------------
    // Connection
    // -------------------------------------------------------------------------
    public ObservableCollection<SavedConnection> SavedConnections { get; } = [];
    [ObservableProperty] private SavedConnection? _selectedConnection;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectedDatabaseLabel = "Not connected";

    // -------------------------------------------------------------------------
    // Object tree (SSMS-style explorer)
    // -------------------------------------------------------------------------
    public ObservableCollection<ManagerNode> ManagerTreeRoots { get; } = [];
    [ObservableProperty] private ManagerNode? _managerSelectedNode;
    [ObservableProperty] private DbObjectInfo? _selectedObject;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoadingTree;

    // ─── Object Explorer filter (applied server-side when folders load) ───
    [ObservableProperty] private string _objectNameFilter = string.Empty;
    [ObservableProperty] private string _objectSchemaFilter = string.Empty;
    [ObservableProperty] private string _objectTypeFilter = "(All types)";
    [ObservableProperty] private bool _isExplorerFilterActive;
    [ObservableProperty] private string _explorerFilterBadge = string.Empty;
    public string[] ObjectTypeFilterOptions { get; } =
        ["(All types)", "Tables", "Views", "Stored Procedures", "Functions", "Triggers"];
    public ICommand ClearExplorerFilterCommand { get; }

    private ConnectionInfo? _connInfo;

    /// <summary>Set when the explorer is pointed at a sibling database. It overrides
    /// the saved profile for every command, so the tree, the data grid and the
    /// generated scripts can never disagree about which database they address.
    /// The stored profile itself is never rewritten.</summary>
    private string? _activeDatabase;

    /// <summary>The connection every command in this window uses.</summary>
    private ConnectionInfo CurrentConnection()
    {
        var info = SelectedConnection?.ToConnectionInfo() ?? _connInfo
            ?? throw new InvalidOperationException("Connect to a server first.");
        return _activeDatabase == null ||
               string.Equals(_activeDatabase, info.Database, StringComparison.OrdinalIgnoreCase)
            ? info
            : info.ForDatabase(_activeDatabase);
    }

    /// <summary>The database node the explorer is working in, or null before the
    /// tree loads. Callers must not assume it is a top-level root any more — the
    /// server node and the Databases folder sit above it.</summary>
    public ManagerNode? CurrentDatabaseNode => FindDatabaseRoot();

    /// <summary>The database node the tree is currently working in. The server root
    /// and the Databases folder sit above it, so this has to search.</summary>
    private ManagerNode? FindDatabaseRoot()
    {
        var queue = new Queue<ManagerNode>(ManagerTreeRoots);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.Kind == NodeKind.Root) return node;
            foreach (var child in node.Children) queue.Enqueue(child);
        }
        return null;
    }
    private string? _pendingDefinition;

    // Dialog hosts (wired by DbManagerWindow)
    public Func<string, string, TableMetadata, Task<IndexSpec?>>? ShowNewIndexDialogAsync;
    public Func<string, string, TableMetadata, Task<PartitionSpec?>>? ShowPartitionDialogAsync;
    public Func<DesignerKind, Task<DesignerSpec?>>? ShowObjectDesignerAsync;
    public Func<string, string, string, Task<bool>>? ShowScriptConfirmAsync;
    public Func<DatabaseProperties, Task>? ShowDbPropertiesAsync;
    public Func<TableProperties, Task>? ShowTablePropertiesAsync;
    public Func<ObjectDependencies, Task>? ShowDependenciesAsync;
    public Func<Task<string?>>? PickBackupFileAsync;
    public Func<RestoreDraft, Task<RestorePlan?>>? ShowRestoreDialogAsync;
    public Func<BackupDraft, Task<BackupRequest?>>? ShowBackupDialogAsync;

    // -------------------------------------------------------------------------
    // Active tab
    // -------------------------------------------------------------------------
    [ObservableProperty] private ManagerTab _activeTab = ManagerTab.Data;
    [ObservableProperty] private bool _isTabData = true;
    [ObservableProperty] private bool _isTabStructure;
    [ObservableProperty] private bool _isTabDefinition;
    [ObservableProperty] private bool _isTabExecute;
    [ObservableProperty] private bool _showExecuteTab;   // only for stored procs

    // -------------------------------------------------------------------------
    // Data tab
    // -------------------------------------------------------------------------
    public ObservableCollection<string> TableColumnNames { get; } = [];
    public ObservableCollection<Dictionary<string, object?>> TableRows { get; } = [];
    public ObservableCollection<TableFilter> TableFilters { get; } = [];

    [ObservableProperty] private int _topNRows = 500;
    [ObservableProperty] private string _dataStatusText = string.Empty;
    [ObservableProperty] private int _loadedRowCount;
    [ObservableProperty] private long _totalTableRowCount;
    [ObservableProperty] private bool _hasRowCountInfo;
    [ObservableProperty] private bool _isLoadingData;
    [ObservableProperty] private bool _hasTableData;

    // Row editing / deletion
    [ObservableProperty] private Dictionary<string, object?>? _selectedRow;
    [ObservableProperty] private bool _hasSelectedRow;
    [ObservableProperty] private bool _hasPendingEdits;
    private readonly Dictionary<Dictionary<string, object?>, Dictionary<string, object?>> _pendingRowEdits = new();
    private readonly Dictionary<Dictionary<string, object?>, Dictionary<string, object?>> _originalRowValues = new();

    public ICommand SaveTableChangesCommand { get; }
    public ICommand DeleteRowCommand { get; }

    public static int[] TopNOptions => [100, 200, 500, 1000, 5000, 0]; // 0 = ALL

    // -------------------------------------------------------------------------
    // Structure tab
    // -------------------------------------------------------------------------
    public ObservableCollection<TableColumn> StructureColumns { get; } = [];
    [ObservableProperty] private bool _hasStructure;

    // -------------------------------------------------------------------------
    // Definition tab (View / Modify SQL)
    // -------------------------------------------------------------------------
    [ObservableProperty] private string _objectDefinition = string.Empty;
    [ObservableProperty] private bool _hasDefinition;
    [ObservableProperty] private bool _isSavingDefinition;
    [ObservableProperty] private string _definitionSaveStatus = string.Empty;

    // -------------------------------------------------------------------------
    // Execute tab (Stored Procs)
    // -------------------------------------------------------------------------
    public ObservableCollection<StoredProcParam> ProcParams { get; } = [];
    public ObservableCollection<Dictionary<string, object?>> ExecResultRows { get; } = [];
    public ObservableCollection<string> ExecResultColumns { get; } = [];
    [ObservableProperty] private string _execOutputText = string.Empty;
    [ObservableProperty] private bool _hasExecResults;
    [ObservableProperty] private bool _isExecuting;

    // -------------------------------------------------------------------------
    // General status / error
    // -------------------------------------------------------------------------
    [ObservableProperty] private string _statusMessage = "Select a connection and click Connect.";
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Set by the window to enable clipboard operations.</summary>
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------
    public ICommand ConnectCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ApplyFiltersCommand { get; }
    public ICommand AddFilterCommand { get; }
    public ICommand RemoveFilterCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ExecuteProcCommand { get; }
    public ICommand CopyDefinitionCommand { get; }
    public ICommand SaveDefinitionCommand { get; }
    public ICommand SwitchToDataTabCommand { get; }
    public ICommand SwitchToStructureTabCommand { get; }
    public ICommand SwitchToDefinitionTabCommand { get; }
    public ICommand SwitchToExecuteTabCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }

    // Explorer context actions
    public ICommand OpenNodeCommand { get; }
    public ICommand DoubleTapNodeCommand { get; }
    public ICommand TablePropertiesCommand { get; }
    public ICommand ViewDependenciesCommand { get; }
    public ICommand SelectTopRowsCommand { get; }
    public ICommand EditTopRowsCommand { get; }
    public ICommand NewIndexCommand { get; }
    public ICommand NewObjectCommand { get; }
    public ICommand CreatePartitionCommand { get; }
    public ICommand ScriptCreateCommand { get; }
    public ICommand ScriptSelectCommand { get; }
    public ICommand ScriptInsertCommand { get; }
    public ICommand ScriptUpdateCommand { get; }
    public ICommand ScriptDeleteCommand { get; }
    public ICommand ScriptExecCommand { get; }
    public ICommand ScriptCreateOrAlterCommand { get; }
    public ICommand ScriptDropCommand { get; }
    public ICommand RebuildIndexCommand { get; }
    public ICommand DisableIndexCommand { get; }
    public ICommand EnableIndexCommand { get; }
    public ICommand DropIndexCommand { get; }
    public ICommand UpdateStatsCommand { get; }
    public ICommand RefreshNodeCommand { get; }
    public ICommand DatabasePropertiesCommand { get; }
    public ICommand ShrinkDatabaseCommand { get; }
    public ICommand RestoreDatabaseCommand { get; }
    public ICommand BackupDatabaseCommand { get; }

    // Server-scope actions
    public ICommand UseAsCurrentDatabaseCommand { get; }
    public ICommand StartAgentJobCommand { get; }
    public ICommand ToggleAgentJobCommand { get; }

    public DbManagerViewModel()
    {
        ConnectCommand              = new AsyncRelayCommand(ConnectAsync);
        RefreshCommand              = new AsyncRelayCommand(RefreshAsync);
        ApplyFiltersCommand         = new AsyncRelayCommand(ApplyFiltersAsync);
        AddFilterCommand            = new RelayCommand(AddFilter);
        RemoveFilterCommand         = new RelayCommand<TableFilter?>(RemoveFilter);
        ClearFiltersCommand         = new RelayCommand(ClearFilters);
        ExecuteProcCommand          = new AsyncRelayCommand(ExecuteProcAsync);
        CopyDefinitionCommand       = new AsyncRelayCommand(CopyDefinitionAsync);
        SaveDefinitionCommand       = new AsyncRelayCommand(SaveDefinitionAsync);
        SwitchToDataTabCommand      = new AsyncRelayCommand(() => SwitchTabAsync(ManagerTab.Data));
        SwitchToStructureTabCommand = new AsyncRelayCommand(() => SwitchTabAsync(ManagerTab.Structure));
        SwitchToDefinitionTabCommand = new AsyncRelayCommand(() => SwitchTabAsync(ManagerTab.Definition));
        SwitchToExecuteTabCommand   = new AsyncRelayCommand(() => SwitchTabAsync(ManagerTab.Execute));
        SearchCommand               = new AsyncRelayCommand(SearchAsync);
        ClearSearchCommand          = new AsyncRelayCommand(ClearSearchAsync);
        ClearExplorerFilterCommand  = new RelayCommand(ClearExplorerFilter);
        SaveTableChangesCommand     = new AsyncRelayCommand(SaveTableChangesAsync, () => HasPendingEdits);
        DeleteRowCommand            = new AsyncRelayCommand(DeleteSelectedRowAsync, () => HasSelectedRow);

        OpenNodeCommand         = new AsyncRelayCommand<ManagerNode?>(OpenNodeAsync);
        DoubleTapNodeCommand    = new AsyncRelayCommand<ManagerNode?>(DoubleTapNodeAsync);
        TablePropertiesCommand  = new AsyncRelayCommand<ManagerNode?>(TablePropertiesAsync);
        ViewDependenciesCommand = new AsyncRelayCommand<ManagerNode?>(ViewDependenciesAsync);
        SelectTopRowsCommand    = new RelayCommand<ManagerNode?>(n => OpenWithTopN(n, 1000));
        EditTopRowsCommand      = new RelayCommand<ManagerNode?>(n => OpenWithTopN(n, 200));
        NewIndexCommand         = new AsyncRelayCommand<ManagerNode?>(NewIndexAsync);
        NewObjectCommand        = new AsyncRelayCommand<ManagerNode?>(NewObjectAsync);
        CreatePartitionCommand  = new AsyncRelayCommand<ManagerNode?>(CreatePartitionAsync);
        ScriptCreateCommand     = new AsyncRelayCommand<ManagerNode?>(ScriptCreateAsync);
        ScriptSelectCommand     = new AsyncRelayCommand<ManagerNode?>(ScriptSelectAsync);
        ScriptInsertCommand     = new AsyncRelayCommand<ManagerNode?>(ScriptInsertAsync);
        ScriptUpdateCommand     = new AsyncRelayCommand<ManagerNode?>(ScriptUpdateAsync);
        ScriptDeleteCommand     = new AsyncRelayCommand<ManagerNode?>(ScriptDeleteAsync);
        ScriptExecCommand       = new AsyncRelayCommand<ManagerNode?>(ScriptExecAsync);
        ScriptCreateOrAlterCommand = new AsyncRelayCommand<ManagerNode?>(ScriptCreateOrAlterAsync);
        ScriptDropCommand       = new AsyncRelayCommand<ManagerNode?>(ScriptDropAsync);
        RebuildIndexCommand     = new AsyncRelayCommand<ManagerNode?>(n => RunIndexActionAsync(n,
            "Rebuild index", "Rebuilds the index (online read is blocked briefly).",
            ManagerScriptBuilder.RebuildIndex));
        DisableIndexCommand     = new AsyncRelayCommand<ManagerNode?>(n => RunIndexActionAsync(n,
            "Disable index", "Queries that rely on this index will fall back to scans until it is re-enabled.",
            ManagerScriptBuilder.DisableIndex));
        EnableIndexCommand      = new AsyncRelayCommand<ManagerNode?>(n => RunIndexActionAsync(n,
            "Enable index", "Re-enables the index via REBUILD.",
            ManagerScriptBuilder.EnableIndex));
        DropIndexCommand        = new AsyncRelayCommand<ManagerNode?>(n => RunIndexActionAsync(n,
            "Drop index", "⚠ The index will be permanently dropped. Queries relying on it may slow down.",
            ManagerScriptBuilder.DropIndex));
        UpdateStatsCommand      = new AsyncRelayCommand<ManagerNode?>(UpdateStatsAsync);
        RefreshNodeCommand      = new AsyncRelayCommand<ManagerNode?>(RefreshNodeCommandAsync);
        DatabasePropertiesCommand = new AsyncRelayCommand<ManagerNode?>(DatabasePropertiesAsync);
        ShrinkDatabaseCommand   = new AsyncRelayCommand<ManagerNode?>(ShrinkDatabaseAsync);
        RestoreDatabaseCommand  = new AsyncRelayCommand<ManagerNode?>(RestoreDatabaseAsync);
        BackupDatabaseCommand   = new AsyncRelayCommand<ManagerNode?>(BackupDatabaseAsync);
        UseAsCurrentDatabaseCommand = new AsyncRelayCommand<ManagerNode?>(UseAsCurrentDatabaseAsync);
        StartAgentJobCommand        = new AsyncRelayCommand<ManagerNode?>(StartAgentJobAsync);
        ToggleAgentJobCommand       = new AsyncRelayCommand<ManagerNode?>(ToggleAgentJobAsync);

        LoadSavedConnections();
    }

    partial void OnSelectedRowChanged(Dictionary<string, object?>? value)
    {
        HasSelectedRow = value != null;
        ((AsyncRelayCommand)DeleteRowCommand).NotifyCanExecuteChanged();
    }

    partial void OnHasPendingEditsChanged(bool value)
    {
        ((AsyncRelayCommand)SaveTableChangesCommand).NotifyCanExecuteChanged();
    }

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------
    private void LoadSavedConnections()
    {
        var list = _savedService.Load();
        foreach (var c in list)
            SavedConnections.Add(c);
        SelectedConnection = SavedConnections.FirstOrDefault();
    }

    // -------------------------------------------------------------------------
    // Connect / refresh
    // -------------------------------------------------------------------------
    private async Task ConnectAsync()
    {
        if (SelectedConnection == null) return;
        await RunSafeAsync(async ct =>
        {
            IsLoadingTree = true;
            StatusMessage = $"Connecting to {SelectedConnection.DisplayName}…";
            // Connect always lands on the database the profile names.
            _activeDatabase = null;
            var info = CurrentConnection();
            await LoadObjectTreeAsync(info, ct);
            IsConnected = true;
            StatusMessage = "Connected. Click an object to explore.";
        });
        IsLoadingTree = false;
    }

    private async Task RefreshAsync()
    {
        if (SelectedConnection == null || !IsConnected) return;
        await RunSafeAsync(async ct =>
        {
            IsLoadingTree = true;
            StatusMessage = "Refreshing…";
            var info = CurrentConnection();
            await LoadObjectTreeAsync(info, ct);
            StatusMessage = "Object tree refreshed.";
        });
        IsLoadingTree = false;
    }

    private async Task LoadObjectTreeAsync(ConnectionInfo info, CancellationToken ct)
    {
        _connInfo = info;
        _activeDatabase = info.Database;
        UpdateExplorerFilterState();
        ManagerTreeRoots.Clear();
        ConnectedDatabaseLabel = $"{info.Server} / {info.Database}";

        // Server scope sits above the database: the same tree SSMS opens with.
        var serverRoot = new ManagerNode
        {
            Kind = NodeKind.ServerRoot,
            Label = info.Server,
            Detail = info.SafeForLog,
            Icon = "🖥",
            IsExpanded = true
        };
        ManagerTreeRoots.Add(serverRoot);

        // The Databases folder holds the connected database, so it loads with the
        // tree; the other server folders stay lazy.
        var databases = MakeDatabasesFolder(serverRoot);
        serverRoot.Children.Add(databases);
        serverRoot.Children.Add(MakeServerSecurityFolder(serverRoot));
        serverRoot.Children.Add(MakeAgentFolder(serverRoot));
        serverRoot.Children.Add(MakeLinkedServersFolder(serverRoot));
        await LoadDatabasesFolderAsync(databases, info, ct);

        _ = AttachServerOverviewAsync(serverRoot, info);
    }

    /// <summary>Annotates the server node with what the instance says about itself.
    /// A version probe must never fail a connection, so a failure only costs the label.</summary>
    private async Task AttachServerOverviewAsync(ManagerNode serverRoot, ConnectionInfo info)
    {
        try
        {
            var overview = await _service.GetServerOverviewAsync(info, CancellationToken.None);
            serverRoot.Detail = $"SQL Server {overview.Version} · {overview.Edition}";
            serverRoot.Label = string.IsNullOrWhiteSpace(overview.Version)
                ? info.Server
                : $"{info.Server} ({overview.Version})";
        }
        catch (Exception ex)
        {
            AppLog.Info($"[Manager] Server overview unavailable: {ex.Message}");
        }
    }

    // ─── Server-scope folders (all lazy, all read-only) ──────────────────────

    /// <summary>The connected database keeps its own node — and the object folders
    /// already loaded under it — so a refresh reloads the siblings alongside it.</summary>
    private ManagerNode MakeDatabasesFolder(ManagerNode parent)
    {
        var folder = new ManagerNode
        {
            Kind = NodeKind.DatabasesFolder, Label = "Databases", Icon = "🗃",
            Parent = parent, IsExpanded = true
        };
        folder.Loader = ct =>
        {
            if (_connInfo == null) return Task.CompletedTask;
            return LoadDatabasesFolderAsync(folder, _connInfo, ct);
        };
        return folder;
    }

    /// <summary>Databases in SSMS order of importance: the one the explorer is
    /// working in first and fully loaded, then every sibling as a leaf.</summary>
    private async Task LoadDatabasesFolderAsync(ManagerNode folder, ConnectionInfo info, CancellationToken ct)
    {
        folder.Children.Clear();
        folder.IsLoading = true;
        try
        {
            var current = new ManagerNode
            {
                Kind = NodeKind.Root, Label = info.Database, Name = info.Database,
                Icon = "🗄", Parent = folder, IsExpanded = true
            };
            folder.Children.Add(current);
            await LoadDatabaseFoldersAsync(current, info, ct);

            var dbs = await _service.GetDatabasesAsync(info, ct);
            foreach (var db in dbs)
            {
                if (string.Equals(db.Name, info.Database, StringComparison.OrdinalIgnoreCase)) continue;
                folder.Children.Add(new ManagerNode
                {
                    Kind = NodeKind.DatabaseNode, Label = db.Name,
                    Detail = $"{db.State} · {db.SizeMb:N0} MB",
                    Icon = db.IsOnline ? "🗄" : "⏸",
                    Name = db.Name, Parent = folder
                });
            }
            folder.Label = $"Databases ({dbs.Count})";
        }
        finally
        {
            folder.IsLoading = false;
        }
    }

    /// <summary>Server logins and server roles. Distinct from the database Security
    /// folder: these come from sys.server_principals on master.</summary>
    private ManagerNode MakeServerSecurityFolder(ManagerNode parent)
    {
        var folder = new ManagerNode
        {
            Kind = NodeKind.ServerSecurityFolder, Label = "Security", Icon = "🛡", Parent = parent
        };
        folder.Children.Add(new ManagerNode { Kind = NodeKind.Column, Label = "Loading…", Icon = "⏳" });
        folder.Loader = async ct =>
        {
            folder.Children.Clear();
            if (_connInfo == null) return;
            folder.IsLoading = true;
            try
            {
                var (logins, roles) = await _service.GetServerSecurityAsync(_connInfo, ct);

                var loginsFolder = new ManagerNode
                {
                    Kind = NodeKind.ServerSecurityFolder, Label = $"Logins ({logins.Count})",
                    Icon = "👤", Parent = folder
                };
                foreach (var l in logins)
                    loginsFolder.Children.Add(new ManagerNode
                    {
                        Kind = NodeKind.Login, Label = l.Label,
                        Detail = $"{l.TypeDesc} · default {l.DefaultDatabase}",
                        Icon = l.IsDisabled ? "⏸" : l.IsWindowsLogin ? "🖥" : "👤",
                        Name = l.Name, Parent = loginsFolder
                    });

                var rolesFolder = new ManagerNode
                {
                    Kind = NodeKind.ServerSecurityFolder, Label = $"Server Roles ({roles.Count})",
                    Icon = "🛡", Parent = folder
                };
                foreach (var r in roles)
                    rolesFolder.Children.Add(new ManagerNode
                    {
                        Kind = NodeKind.ServerRole, Label = r.Name, Detail = r.TypeDesc,
                        Icon = "🛡", Name = r.Name, Parent = rolesFolder
                    });

                folder.Children.Add(loginsFolder);
                folder.Children.Add(rolesFolder);
            }
            finally
            {
                folder.IsLoading = false;
            }
        };
        return folder;
    }

    /// <summary>SQL Agent: the job list, or an explicit statement that this instance
    /// has no Agent rather than a swallowed error.</summary>
    private ManagerNode MakeAgentFolder(ManagerNode parent)
    {
        var folder = new ManagerNode
        {
            Kind = NodeKind.AgentFolder, Label = "SQL Agent", Icon = "🤖", Parent = parent
        };
        folder.Children.Add(new ManagerNode { Kind = NodeKind.Column, Label = "Loading…", Icon = "⏳" });
        folder.Loader = async ct =>
        {
            folder.Children.Clear();
            if (_connInfo == null) return;
            folder.IsLoading = true;
            try
            {
                var jobs = await _service.GetAgentJobsAsync(_connInfo, ct);
                if (jobs == null)
                {
                    folder.Children.Add(new ManagerNode
                    {
                        Kind = NodeKind.Column, Icon = "ℹ",
                        Label = "SQL Agent is not available on this instance"
                    });
                    return;
                }

                var jobsFolder = new ManagerNode
                {
                    Kind = NodeKind.AgentJobsFolder, Label = $"Jobs ({jobs.Count})",
                    Icon = "📋", Parent = folder
                };
                foreach (var j in jobs)
                    jobsFolder.Children.Add(new ManagerNode
                    {
                        Kind = NodeKind.AgentJob, Label = j.Name, Detail = j.Detail,
                        Icon = j.Enabled ? "▶" : "⏸",
                        Name = j.Name, JobEnabled = j.Enabled, Parent = jobsFolder
                    });
                folder.Children.Add(jobsFolder);
            }
            finally
            {
                folder.IsLoading = false;
            }
        };
        return folder;
    }

    private ManagerNode MakeLinkedServersFolder(ManagerNode parent)
    {
        var folder = new ManagerNode
        {
            Kind = NodeKind.LinkedServersFolder, Label = "Linked Servers", Icon = "🔗", Parent = parent
        };
        folder.Children.Add(new ManagerNode { Kind = NodeKind.Column, Label = "Loading…", Icon = "⏳" });
        folder.Loader = async ct =>
        {
            folder.Children.Clear();
            if (_connInfo == null) return;
            folder.IsLoading = true;
            try
            {
                var servers = await _service.GetLinkedServersAsync(_connInfo, ct);
                foreach (var s in servers)
                    folder.Children.Add(new ManagerNode
                    {
                        Kind = NodeKind.LinkedServer,
                        Label = s.Name,
                        Detail = s.IsLocal ? "(local) — this instance" : $"{s.Product} · {s.DataSource}",
                        Icon = s.IsLocal ? "🖥" : "🌐",
                        Name = s.Name, Parent = folder
                    });
                folder.Label = $"Linked Servers ({servers.Count})";
            }
            finally
            {
                folder.IsLoading = false;
            }
        };
        return folder;
    }

    /// <summary>Fills a database node with the object folders. Shared by the initial
    /// load and by re-expanding the Databases folder after a refresh.</summary>
    private async Task LoadDatabaseFoldersAsync(ManagerNode dbNode, ConnectionInfo info, CancellationToken ct)
    {
        var filter = BuildExplorerFilter();
        var only = filter.TypeLabel;
        bool Show(string label) => only == null || only == label;

        if (Show("Tables"))
            dbNode.Children.Add(MakeObjectFolder("Tables", "🗂", NodeKind.TablesFolder,
                await _service.GetObjectsAsync(info, DbObjectType.Table, ct, filter), true));
        if (Show("Views"))
            dbNode.Children.Add(MakeObjectFolder("Views", "👁", NodeKind.ViewsFolder,
                await _service.GetObjectsAsync(info, DbObjectType.View, ct, filter), false));
        if (Show("Stored Procedures"))
            dbNode.Children.Add(MakeObjectFolder("Stored Procedures", "⚙", NodeKind.ProcsFolder,
                await _service.GetObjectsAsync(info, DbObjectType.StoredProcedure, ct, filter), false));
        if (Show("Functions"))
            dbNode.Children.Add(MakeObjectFolder("Functions", "𝑓", NodeKind.FunctionsFolder,
                await _service.GetObjectsAsync(info, DbObjectType.Function, ct, filter), false));
        if (Show("Triggers"))
            dbNode.Children.Add(MakeObjectFolder("Triggers", "⚡", NodeKind.TriggersFolder,
                await _service.GetObjectsAsync(info, DbObjectType.Trigger, ct, filter), false));
        dbNode.Children.Add(MakeSecurityFolder());
    }

    // ─── Explorer filter application ─────────────────────────────────────────

    private ExplorerQueryFilter BuildExplorerFilter() => new(
        string.IsNullOrWhiteSpace(ObjectNameFilter) ? null : ObjectNameFilter.Trim(),
        string.IsNullOrWhiteSpace(ObjectSchemaFilter) ? null : ObjectSchemaFilter.Trim(),
        ObjectTypeFilter is "Tables" or "Views" or "Stored Procedures" or "Functions" or "Triggers"
            ? ObjectTypeFilter
            : null);

    private void UpdateExplorerFilterState()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ObjectNameFilter)) parts.Add($"name ~ {ObjectNameFilter.Trim()}");
        if (!string.IsNullOrWhiteSpace(ObjectSchemaFilter)) parts.Add($"schema ~ {ObjectSchemaFilter.Trim()}");
        var type = BuildExplorerFilter().TypeLabel;
        if (type != null) parts.Add(type);

        IsExplorerFilterActive = parts.Count > 0;
        ExplorerFilterBadge = parts.Count == 0 ? "" : "Filtered: " + string.Join(", ", parts);
    }

    partial void OnObjectNameFilterChanged(string value) => _ = ApplyExplorerFilterAsync();
    partial void OnObjectSchemaFilterChanged(string value) => _ = ApplyExplorerFilterAsync();
    partial void OnObjectTypeFilterChanged(string value) => _ = ApplyExplorerFilterAsync();

    private bool _suppressFilterRefresh;
    private bool _filterRefreshRunning;
    private bool _filterRefreshPending;

    /// <summary>Re-queries the tree with the current filter while keeping the nodes
    /// the user already expanded open — no reconnect needed. Rapid successive edits
    /// coalesce into one trailing reload.</summary>
    private async Task ApplyExplorerFilterAsync()
    {
        UpdateExplorerFilterState();
        if (_suppressFilterRefresh || !IsConnected || SelectedConnection == null) return;
        if (_filterRefreshRunning) { _filterRefreshPending = true; return; }
        _filterRefreshRunning = true;
        try
        {
            do
            {
                _filterRefreshPending = false;
                await RefreshTreeWithFilterAsync();
            }
            while (_filterRefreshPending);
        }
        finally
        {
            _filterRefreshRunning = false;
        }
    }

    private async Task RefreshTreeWithFilterAsync()
    {
        var info = CurrentConnection();
        await RunSafeAsync(async ct =>
        {
            IsLoadingTree = true;
            try
            {
                await LoadExpandedAsync(info, ct);
                StatusMessage = IsExplorerFilterActive
                    ? $"Object tree refreshed — {ExplorerFilterBadge}."
                    : "Object tree refreshed — filter cleared, full object list restored.";
            }
            finally
            {
                IsLoadingTree = false;
            }
        });
    }

    /// <summary>Reloads the tree and re-expands the table/view nodes that were
    /// open before, matched by schema + name.</summary>
    private async Task LoadExpandedAsync(ConnectionInfo info, CancellationToken ct)
    {
        var oldRoot = FindDatabaseRoot();
        if (oldRoot == null)
        {
            await LoadObjectTreeAsync(info, ct);
            return;
        }
        var openNodes = oldRoot.Children.ToDictionary(
            folder => folder.Kind,
            folder => folder.Children.Where(n => n.IsExpanded)
                       .Select(n => (n.Schema, n.Name))
                       .ToHashSet());

        await LoadObjectTreeAsync(info, ct);

        var root = FindDatabaseRoot();
        if (root == null) return;
        foreach (var folder in root.Children)
        {
            if (!openNodes.TryGetValue(folder.Kind, out var keys)) continue;
            foreach (var node in folder.Children)
                if (keys.Contains((node.Schema, node.Name)))
                    node.IsExpanded = true;
        }
    }

    private void ClearExplorerFilter()
    {
        _suppressFilterRefresh = true;
        ObjectNameFilter = string.Empty;
        ObjectSchemaFilter = string.Empty;
        ObjectTypeFilter = "(All types)";
        _suppressFilterRefresh = false;
        _ = ApplyExplorerFilterAsync();
    }

    /// <summary>SSMS-style Security folder: Users + Roles, read-only, lazy-loaded
    /// on first expand from sys.database_principals.</summary>
    private ManagerNode MakeSecurityFolder()
    {
        var folder = new ManagerNode { Kind = NodeKind.SecurityFolder, Label = "Security", Icon = "🛡" };
        folder.Children.Add(new ManagerNode { Kind = NodeKind.Column, Label = "Loading…", Icon = "⏳" });
        folder.Loader = async ct =>
        {
            folder.Children.Clear();
            if (_connInfo == null)
            {
                folder.Children.Add(new ManagerNode { Kind = NodeKind.Column, Label = "Not connected", Icon = "•" });
                return;
            }
            folder.IsLoading = true;
            try
            {
                var (users, roles) = await _service.GetDbSecurityAsync(_connInfo, ct);

                var usersFolder = new ManagerNode
                {
                    Kind = NodeKind.SecurityFolder, Label = $"Users ({users.Count})", Icon = "👥",
                    Parent = folder
                };
                foreach (var u in users)
                    usersFolder.Children.Add(new ManagerNode
                    {
                        Kind = NodeKind.DbUser, Label = u.Name, Detail = u.Detail,
                        Icon = u.TypeDesc.Contains("WINDOWS") ? "🖥" : "👤", Name = u.Name
                    });
                var rolesFolder = new ManagerNode
                {
                    Kind = NodeKind.SecurityFolder, Label = $"Roles ({roles.Count})", Icon = "🛡",
                    Parent = folder
                };
                foreach (var r in roles)
                    rolesFolder.Children.Add(new ManagerNode
                    {
                        Kind = NodeKind.DbRole, Label = r.Name, Detail = r.Detail,
                        Icon = r.IsFixedRole ? "🛡" : "🛠", Name = r.Name
                    });
                folder.Children.Add(usersFolder);
                folder.Children.Add(rolesFolder);
            }
            finally
            {
                folder.IsLoading = false;
            }
        };
        return folder;
    }

    private ManagerNode MakeObjectFolder(string label, string icon, NodeKind kind,
        List<DbObjectInfo> items, bool expanded)
    {
        var folder = new ManagerNode { Kind = kind, Label = $"{label} ({items.Count})", Icon = icon };
        foreach (var it in items)
        {
            var n = MakeObjectNode(it);
            n.Parent = folder;
            folder.Children.Add(n);
        }
        if (expanded) folder.IsExpanded = true;
        return folder;
    }

    private ManagerNode MakeObjectNode(DbObjectInfo obj)
    {
        if (obj.ObjectType is DbObjectType.Table or DbObjectType.View)
        {
            var node = new ManagerNode
            {
                Kind = obj.ObjectType == DbObjectType.Table ? NodeKind.Table : NodeKind.View,
                Label = obj.DisplayName,
                Icon = obj.TypeIcon,
                Schema = obj.Schema,
                Name = obj.Name
            };
            AttachTableFolders(node);
            return node;
        }
        return new ManagerNode
        {
            Kind = obj.ObjectType switch
            {
                DbObjectType.StoredProcedure => NodeKind.Proc,
                DbObjectType.Function       => NodeKind.Function,
                _                           => NodeKind.TriggerObj
            },
            Label = obj.DisplayName,
            Icon = obj.TypeIcon,
            Schema = obj.Schema,
            Name = obj.Name
        };
    }

    /// <summary>Adds the six SSMS-style metadata folders under a table/view node.
    /// Each folder loads its leaf items lazily on first expand.</summary>
    private void AttachTableFolders(ManagerNode tableNode)
    {
        var (sch, tbl) = (tableNode.Schema, tableNode.Name);

        ManagerNode Folder(NodeKind kind, string label, string icon,
            Func<ConnectionInfo, string, string, CancellationToken, Task<List<ManagerNode>>> loader)
        {
            var f = new ManagerNode { Kind = kind, Label = label, Icon = icon, Schema = sch, Name = tbl };
            f.Children.Add(new ManagerNode { Kind = NodeKind.Column, Label = "Loading…", Icon = "⏳" });
            f.Loader = async ct =>
            {
                f.Children.Clear();
                if (_connInfo == null)
                {
                    f.Children.Add(new ManagerNode { Kind = NodeKind.Column, Label = "Not connected", Icon = "•" });
                    return;
                }
                f.IsLoading = true;
                try
                {
                    foreach (var n in await loader(_connInfo, sch, tbl, ct))
                    {
                        n.Parent = f;
                        f.Children.Add(n);
                    }
                }
                finally
                {
                    f.IsLoading = false;
                }
            };
            return f;
        }

        var folders = new List<ManagerNode>
        {
            Folder(NodeKind.ColumnsFolder,    "Columns",    "📋", LoadColumnNodesAsync),
            Folder(NodeKind.KeysFolder,       "Keys",       "🔑", LoadKeyNodesAsync),
            Folder(NodeKind.IndexesFolder,    "Indexes",    "🗃", LoadIndexNodesAsync),
            Folder(NodeKind.TriggersFolder,   "Triggers",   "⚡", LoadTriggerNodesAsync),
            Folder(NodeKind.StatsFolder,      "Statistics", "📈", LoadStatNodesAsync),
            Folder(NodeKind.PartitionsFolder, "Partitions", "🧩", LoadPartitionNodesAsync),
        };
        foreach (var f in folders)
            f.Parent = tableNode;
        tableNode.Children = new ObservableCollection<ManagerNode>(folders);
    }

    // ─── Folder leaf builders ────────────────────────────────────────────────

    private async Task<List<ManagerNode>> LoadColumnNodesAsync(
        ConnectionInfo info, string schema, string table, CancellationToken ct)
    {
        var meta = await _service.GetTableMetadataAsync(info, schema, table, ct);
        var fkCols = meta.Keys.Where(k => k.Kind == "FK").SelectMany(k => k.Columns).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return meta.Columns.Select(c => new ManagerNode
        {
            Kind = NodeKind.Column,
            Label = c.Name,
            Detail = $"{c.DisplayType}{(c.IsNullable ? ", null" : ", not null")}{(c.IsIdentity ? ", identity" : "")}",
            Icon = c.IsPrimaryKey ? "🔑" : fkCols.Contains(c.Name) ? "🔗" : "▫",
            Schema = schema, Name = c.Name,
            IsPrimaryKey = c.IsPrimaryKey,
            IsForeignKey = fkCols.Contains(c.Name),
            ColumnType = c.DisplayType
        }).Cast<ManagerNode>().ToList();
    }

    private async Task<List<ManagerNode>> LoadKeyNodesAsync(
        ConnectionInfo info, string schema, string table, CancellationToken ct)
    {
        var meta = await _service.GetTableMetadataAsync(info, schema, table, ct);
        return meta.Keys.Select(k => new ManagerNode
        {
            Kind = NodeKind.Key,
            Label = k.Name,
            Detail = k.Kind switch
            {
                "PK" => $"Primary key ({string.Join(", ", k.Columns)})",
                "UQ" => $"Unique ({string.Join(", ", k.Columns)})",
                _    => $"Foreign key → {k.ReferencedTable} ({string.Join(", ", k.Columns)})"
            },
            Icon = k.Kind switch { "PK" => "🔑", "UQ" => "✨", _ => "🔗" },
            Schema = schema, Name = k.Name
        }).Cast<ManagerNode>().ToList();
    }

    private async Task<List<ManagerNode>> LoadIndexNodesAsync(
        ConnectionInfo info, string schema, string table, CancellationToken ct)
    {
        var meta = await _service.GetTableMetadataAsync(info, schema, table, ct);
        return meta.Indexes.Select(ix =>
        {
            var bits = new List<string> { ix.TypeDesc.ToLowerInvariant().Replace('_', ' ') };
            if (ix.IsUnique) bits.Add("unique");
            if (ix.IsDisabled) bits.Add("disabled");
            if (ix.Filter != null) bits.Add($"filter: {ix.Filter}");
            var detail = string.Join(", ", bits) + $" — key: ({string.Join(", ", ix.KeyColumns)})";
            if (ix.IncludedColumns.Count > 0)
                detail += $" include: ({string.Join(", ", ix.IncludedColumns)})";
            return new ManagerNode
            {
                Kind = NodeKind.IndexObj,
                Label = ix.Name,
                Detail = detail,
                Icon = ix.IsDisabled ? "⚠" : ix.IsPrimaryKey ? "🔑" : "🗃",
                Schema = schema, Name = ix.Name,
                IndexType = ix.TypeDesc,
                IsUnique = ix.IsUnique,
                IsDisabled = ix.IsDisabled,
                IsPrimaryKeyIndex = ix.IsPrimaryKey || ix.IsUniqueConstraint,
                IndexFilter = ix.Filter
            };
        }).Cast<ManagerNode>().ToList();
    }

    private async Task<List<ManagerNode>> LoadTriggerNodesAsync(
        ConnectionInfo info, string schema, string table, CancellationToken ct)
    {
        var meta = await _service.GetTableMetadataAsync(info, schema, table, ct);
        return meta.Triggers.Select(t => new ManagerNode
        {
            Kind = NodeKind.TriggerChild,
            Label = t,
            Detail = "trigger",
            Icon = "⚡",
            Schema = schema, Name = t
        }).Cast<ManagerNode>().ToList();
    }

    private async Task<List<ManagerNode>> LoadStatNodesAsync(
        ConnectionInfo info, string schema, string table, CancellationToken ct)
    {
        var meta = await _service.GetTableMetadataAsync(info, schema, table, ct);
        return meta.Stats.Select(s => new ManagerNode
        {
            Kind = NodeKind.Stat,
            Label = s.Name,
            Detail = s.LastUpdated == null
                ? "not sampled yet"
                : $"{s.Rows:N0} rows, updated {s.LastUpdated:yyyy-MM-dd HH:mm}",
            Icon = "📈",
            Schema = schema, Name = s.Name
        }).Cast<ManagerNode>().ToList();
    }

    private async Task<List<ManagerNode>> LoadPartitionNodesAsync(
        ConnectionInfo info, string schema, string table, CancellationToken ct)
    {
        var meta = await _service.GetTableMetadataAsync(info, schema, table, ct);
        return meta.Partitions.Select(p => new ManagerNode
        {
            Kind = NodeKind.Partition,
            Label = $"Partition {p.Number}",
            Detail = p.SchemeName == null
                ? $"{p.Rows:N0} rows — {p.Filegroup ?? "PRIMARY"}"
                : $"{p.Rows:N0} rows — boundary: {p.Boundary ?? "<"} — {p.Filegroup ?? "PRIMARY"}",
            Icon = "🧩",
            Schema = schema, Name = $"#{p.Number}"
        }).Cast<ManagerNode>().ToList();
    }

    // -------------------------------------------------------------------------
    // Explorer context actions
    // -------------------------------------------------------------------------
    partial void OnManagerSelectedNodeChanged(ManagerNode? value)
    {
        if (value is { CanOpen: true })
            _ = OpenNodeAsync(value);
    }

    /// <summary>Tree double-click: tables open their Properties dialog (SSMS-style);
    /// a sibling database becomes the explorer's working database; every other
    /// openable node keeps the default open-in-tabs behavior.</summary>
    private async Task DoubleTapNodeAsync(ManagerNode? node)
    {
        if (node == null) return;
        if (node.Kind == NodeKind.Table)
        {
            await TablePropertiesAsync(node);
            return;
        }
        if (node.CanSetCurrentDb)
        {
            await UseAsCurrentDatabaseAsync(node);
            return;
        }
        await OpenNodeAsync(node);
    }

    /// <summary>Points the explorer at a sibling database. Everything else in the
    /// window follows it, because each command resolves its connection through
    /// <see cref="CurrentConnection"/>; the saved profile is left untouched.</summary>
    private async Task UseAsCurrentDatabaseAsync(ManagerNode? node)
    {
        if (node == null || !node.CanSetCurrentDb) return;
        await RunSafeAsync(async ct =>
        {
            _activeDatabase = node.Name;
            SelectedObject  = null;
            StatusMessage   = $"Switching the explorer to {node.Name}…";
            await LoadObjectTreeAsync(CurrentConnection(), ct);
            StatusMessage = $"Object Explorer is working in {node.Name}.";
        });
    }

    // ─── SQL Agent job actions (confirm-gated, like every other script) ──────

    private async Task StartAgentJobAsync(ManagerNode? node)
    {
        if (node == null || node.Kind != NodeKind.AgentJob) return;
        await RunAgentJobActionAsync(node, "Start job",
            $"msdb.dbo.sp_start_job runs “{node.Name}” on the server immediately," +
            " under the SQL Agent service account.",
            ManagerScriptBuilder.StartAgentJob);
    }

    private async Task ToggleAgentJobAsync(ManagerNode? node)
    {
        if (node == null || node.Kind != NodeKind.AgentJob) return;
        var enable = !node.JobEnabled;
        await RunAgentJobActionAsync(node, enable ? "Enable job" : "Disable job",
            enable
                ? $"msdb.dbo.sp_update_job puts “{node.Name}” back on its schedule."
                : $"msdb.dbo.sp_update_job stops “{node.Name}” from running on its schedule;" +
                  " starting it by hand still works.",
            job => ManagerScriptBuilder.SetAgentJobEnabled(job, enable));
    }

    private async Task RunAgentJobActionAsync(ManagerNode node, string title, string warning,
        Func<string, string> build)
    {
        if (_connInfo == null) return;
        string script;
        try
        {
            script = build(node.Name);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            return;
        }

        if (!await ConfirmAsync(title, warning, script))
            return;

        await RunSafeAsync(async ct =>
        {
            StatusMessage = $"{title}: {node.Name}…";
            await _service.ExecuteRawScriptAsync(_connInfo, script, ct);
            StatusMessage = $"✓ {title} accepted for {node.Name}.";
            // The job list is what changed, and it lives on the folder that owns the loader.
            for (var p = node.Parent; p != null; p = p.Parent)
            {
                if (p.Loader == null) continue;
                await ReloadFolderAsync(p);
                break;
            }
        });
    }

    private async Task TablePropertiesAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || !node.CanGetTableProperties) return;
        if (ShowTablePropertiesAsync == null)
        {
            StatusMessage = "Properties dialog unavailable.";
            return;
        }
        TableProperties? props = null;
        await RunSafeAsync(async ct =>
        {
            StatusMessage = $"Reading properties of {node.Schema}.{node.Name}…";
            props = await _service.GetTablePropertiesAsync(_connInfo, node.Schema, node.Name, ct);
            await ShowTablePropertiesAsync(props);
            StatusMessage =
                $"Table properties — {props.Schema}.{props.Name}, {props.Rows:N0} rows, {props.ReservedText} reserved.";
        });
    }

    private async Task ViewDependenciesAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || !node.CanViewDependencies) return;
        if (ShowDependenciesAsync == null)
        {
            StatusMessage = "Dependencies dialog unavailable.";
            return;
        }
        ObjectDependencies? deps = null;
        await RunSafeAsync(async ct =>
        {
            StatusMessage = $"Reading dependencies of {node.Schema}.{node.Name}…";
            deps = await _service.GetDependenciesAsync(_connInfo, node.Schema, node.Name, ct);
            await ShowDependenciesAsync(deps);
            StatusMessage = $"Dependencies — {deps.FullName}: {deps.SummaryText}";
        });
    }

    private Task OpenNodeAsync(ManagerNode? node)
    {
        if (node == null) return Task.CompletedTask;
        var type = node.Kind switch
        {
            NodeKind.Table      => DbObjectType.Table,
            NodeKind.View       => DbObjectType.View,
            NodeKind.Proc       => DbObjectType.StoredProcedure,
            NodeKind.Function   => DbObjectType.Function,
            NodeKind.TriggerObj => DbObjectType.Trigger,
            _                   => (DbObjectType?)null
        };
        if (type == null) return Task.CompletedTask;
        SelectedObject = new DbObjectInfo { Schema = node.Schema, Name = node.Name, ObjectType = type.Value };
        return Task.CompletedTask;
    }

    /// <summary>SSMS "Select Top 1000 Rows" / "Edit Top 200 Rows": open the object's
    /// Data tab pre-set to the requested row cap.</summary>
    private void OpenWithTopN(ManagerNode? node, int topN)
    {
        if (node == null || node.Kind is not (NodeKind.Table or NodeKind.View)) return;
        TopNRows = topN;
        StatusMessage = topN == 200
            ? $"Opening {node.Schema}.{node.Name} for editing (top {topN} rows)."
            : $"Opening top {topN} rows of {node.Schema}.{node.Name}.";
        _ = OpenNodeAsync(node);
    }

    private (string Schema, string Table) ResolveTable(ManagerNode node) =>
        (node.Schema, node.Name);

    private async Task<TableMetadata?> LoadMetaForAsync(string schema, string table)
    {
        if (_connInfo == null) return null;
        TableMetadata? meta = null;
        await RunSafeAsync(async ct => meta = await _service.GetTableMetadataAsync(_connInfo, schema, table, ct));
        return meta;
    }

    private async Task NewIndexAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null) return;
        var (schema, table) = ResolveTable(node);
        var meta = await LoadMetaForAsync(schema, table);
        if (meta == null) return;
        if (ShowNewIndexDialogAsync == null)
        {
            StatusMessage = "Index dialog unavailable.";
            return;
        }

        var spec = await ShowNewIndexDialogAsync(schema, table, meta);
        if (spec == null) return;

        var script = ManagerScriptBuilder.CreateIndex(spec);
        if (spec.ExecuteNow)
        {
            if (!await ConfirmAsync("Create index",
                    $"Creates index {spec.IndexName} on {schema}.{table}.", script))
                return;
            await RunSafeAsync(async ct =>
            {
                StatusMessage = $"Creating index {spec.IndexName}…";
                await _service.ExecuteRawScriptAsync(_connInfo, script, ct);
                StatusMessage = $"✓ Index {spec.IndexName} created on {schema}.{table}.";
                await RefreshFolderAsync(node, NodeKind.IndexesFolder);
            });
        }
        else
        {
            await ShowScriptInDefinitionTabAsync(schema, table, DbObjectType.Table, script);
        }
    }

    private async Task NewObjectAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null) return;
        var kind = node.Kind switch
        {
            NodeKind.ViewsFolder => DesignerKind.View,
            NodeKind.ProcsFolder => DesignerKind.StoredProcedure,
            _ => DesignerKind.Table
        };
        if (ShowObjectDesignerAsync == null)
        {
            StatusMessage = "Object designer unavailable.";
            return;
        }

        var spec = await ShowObjectDesignerAsync(kind);
        if (spec == null) return;

        string script;
        try
        {
            script = ManagerScriptBuilder.CreateObject(spec);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ShowError = true;
            StatusMessage = "The definition is not complete — nothing was scripted.";
            return;
        }

        var full = $"{spec.Schema}.{spec.Name}";
        if (!spec.ExecuteNow)
        {
            var type = spec.Kind switch
            {
                DesignerKind.View => DbObjectType.View,
                DesignerKind.StoredProcedure => DbObjectType.StoredProcedure,
                _ => DbObjectType.Table
            };
            await ShowScriptInDefinitionTabAsync(spec.Schema, spec.Name, type, script);
            return;
        }

        if (!await ConfirmAsync($"Create {spec.Kind}",
                $"Creates {full} in {_connInfo.Database}.", script))
            return;

        await RunSafeAsync(async ct =>
        {
            StatusMessage = $"Creating {full}…";
            await _service.ExecuteRawScriptAsync(_connInfo, script, ct);
            StatusMessage = $"✓ {full} created.";
            await ReloadFolderAsync(node);
        });
    }

    private async Task CreatePartitionAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null) return;
        var (schema, table) = ResolveTable(node);
        var meta = await LoadMetaForAsync(schema, table);
        if (meta == null) return;
        if (ShowPartitionDialogAsync == null)
        {
            StatusMessage = "Partition dialog unavailable.";
            return;
        }

        var spec = await ShowPartitionDialogAsync(schema, table, meta);
        if (spec == null) return;

        var script = ManagerScriptBuilder.CreatePartition(spec);
        if (spec.ExecuteNow)
        {
            if (!await ConfirmAsync("Create partition",
                    $"Creates partition function {spec.FunctionName} and scheme {spec.SchemeName}" +
                    (spec.AlignClusteredIndex ? ", then aligns the table onto it." : "."),
                    script))
                return;
            await RunSafeAsync(async ct =>
            {
                StatusMessage = "Creating partition function/scheme…";
                await _service.ExecuteRawScriptAsync(_connInfo, script, ct);
                StatusMessage = $"✓ Partition function {spec.FunctionName} created.";
                await RefreshFolderAsync(node, NodeKind.PartitionsFolder);
            });
        }
        else
        {
            await ShowScriptInDefinitionTabAsync(schema, table, DbObjectType.Table, script);
        }
    }

    private async Task ScriptCreateAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null) return;

        if (node.Kind == NodeKind.IndexObj)
        {
            var owner = node.Parent;
            if (owner == null) return;
            var meta = await LoadMetaForAsync(owner.Schema, owner.Name);
            if (meta == null) return;
            var ix = meta.Indexes.FirstOrDefault(i => string.Equals(i.Name, node.Name, StringComparison.OrdinalIgnoreCase));
            if (ix == null)
            {
                StatusMessage = $"Index '{node.Name}' no longer exists — refresh the tree.";
                return;
            }
            var script = ManagerScriptBuilder.CreateIndex(new IndexSpec
            {
                Schema = owner.Schema,
                Table = owner.Name,
                IndexName = ix.Name,
                IndexType = ix.TypeDesc switch
                {
                    "CLUSTERED COLUMNSTORE" => "ClusteredColumnstore",
                    "NONCLUSTERED COLUMNSTORE" => "NonclusteredColumnstore",
                    "CLUSTERED" => "Clustered",
                    _ => "Nonclustered"
                },
                IsUnique = ix.IsUnique,
                Filter = ix.Filter,
                KeyColumns = ix.KeyColumns,
                IncludedColumns = ix.IncludedColumns
            });
            await ShowScriptInDefinitionTabAsync(owner.Schema, owner.Name, DbObjectType.Table, script);
            return;
        }

        var type = node.Kind switch
        {
            NodeKind.View => DbObjectType.View,
            NodeKind.Proc => DbObjectType.StoredProcedure,
            NodeKind.Function => DbObjectType.Function,
            NodeKind.TriggerObj => DbObjectType.Trigger,
            _ => DbObjectType.Table
        };
        await RunSafeAsync(async ct =>
        {
            StatusMessage = $"Scripting {node.Schema}.{node.Name}…";
            var def = await _service.GetObjectDefinitionAsync(_connInfo, node.Schema, node.Name, type, ct);
            await ShowScriptInDefinitionTabAsync(node.Schema, node.Name, type, def);
            StatusMessage = $"CREATE script loaded into the Definition tab.";
        });
    }

    // ─── Script As (SSMS-style SELECT/INSERT/UPDATE/DELETE/EXEC/DROP) ───────

    private async Task ScriptSelectAsync(ManagerNode? node)
    {
        if (node == null || !node.CanScriptData) return;
        await ShowScriptInDefinitionTabAsync(node.Schema, node.Name, TypeFor(node),
            ManagerScriptBuilder.SelectTop(node.Schema, node.Name));
        StatusMessage = "SELECT TOP (1000) template loaded into the Definition tab.";
    }

    private async Task ScriptInsertAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || !node.CanScriptData) return;
        var meta = await LoadMetaForAsync(node.Schema, node.Name);
        if (meta == null || meta.Columns.Count == 0)
        {
            StatusMessage = "Could not load columns for the INSERT template.";
            return;
        }
        await ShowScriptInDefinitionTabAsync(node.Schema, node.Name, TypeFor(node),
            ManagerScriptBuilder.InsertTemplate(node.Schema, node.Name,
                meta.Columns.Select(c => c.Name).ToList()));
        StatusMessage = "INSERT template loaded — fill the placeholder values.";
    }

    private async Task ScriptUpdateAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || !node.CanScriptData) return;
        var meta = await LoadMetaForAsync(node.Schema, node.Name);
        if (meta == null || meta.Columns.Count == 0)
        {
            StatusMessage = "Could not load columns for the UPDATE template.";
            return;
        }
        await ShowScriptInDefinitionTabAsync(node.Schema, node.Name, TypeFor(node),
            ManagerScriptBuilder.UpdateTemplate(node.Schema, node.Name,
                meta.Columns.Select(c => c.Name).ToList(), KeyColumnsOf(meta)));
        StatusMessage = "UPDATE template loaded — fill SET values and the key predicate.";
    }

    private async Task ScriptDeleteAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || node.Kind != NodeKind.Table) return;
        var meta = await LoadMetaForAsync(node.Schema, node.Name);
        if (meta == null)
        {
            StatusMessage = "Could not load keys for the DELETE template.";
            return;
        }
        await ShowScriptInDefinitionTabAsync(node.Schema, node.Name, DbObjectType.Table,
            ManagerScriptBuilder.DeleteTemplate(node.Schema, node.Name, KeyColumnsOf(meta)));
        StatusMessage = "DELETE template loaded — fill the key predicate before running.";
    }

    private async Task ScriptExecAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || !node.CanScriptExec) return;
        var type = TypeFor(node);
        await RunSafeAsync(async ct =>
        {
            StatusMessage = $"Loading parameters of {node.Schema}.{node.Name}…";
            var parameters = await _service.GetProcedureParamsAsync(_connInfo, node.Schema, node.Name, ct);
            await ShowScriptInDefinitionTabAsync(node.Schema, node.Name, type,
                ManagerScriptBuilder.ExecTemplate(node.Schema, node.Name,
                    parameters.Select(p => (p.Name, p.DataType)).ToList()));
            StatusMessage = "EXECUTE template loaded — fill the parameter values.";
        });
    }

    private async Task ScriptCreateOrAlterAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || !node.CanScriptCreateOrAlter) return;
        var type = TypeFor(node);
        await RunSafeAsync(async ct =>
        {
            StatusMessage = $"Scripting {node.Schema}.{node.Name}…";
            var def = await _service.GetObjectDefinitionAsync(_connInfo, node.Schema, node.Name, type, ct);
            await ShowScriptInDefinitionTabAsync(node.Schema, node.Name, type,
                ManagerScriptBuilder.ToCreateOrAlter(def));
            StatusMessage = "CREATE OR ALTER script loaded into the Definition tab.";
        });
    }

    private async Task ScriptDropAsync(ManagerNode? node)
    {
        if (node == null || !node.CanScriptDrop) return;
        await ShowScriptInDefinitionTabAsync(node.Schema, node.Name, TypeFor(node),
            ManagerScriptBuilder.DropObject(node.Schema, node.Name, KindSqlName(node.Kind)));
        StatusMessage = "DROP script loaded — review carefully before running.";
    }

    private static DbObjectType TypeFor(ManagerNode node) => node.Kind switch
    {
        NodeKind.View => DbObjectType.View,
        NodeKind.Proc => DbObjectType.StoredProcedure,
        NodeKind.Function => DbObjectType.Function,
        NodeKind.TriggerObj => DbObjectType.Trigger,
        _ => DbObjectType.Table
    };

    private static string KindSqlName(NodeKind kind) => kind switch
    {
        NodeKind.View => "VIEW",
        NodeKind.Proc => "PROCEDURE",
        NodeKind.Function => "FUNCTION",
        NodeKind.TriggerObj => "TRIGGER",
        _ => "TABLE"
    };

    private static List<string> KeyColumnsOf(TableMetadata meta) =>
        meta.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

    /// <summary>
    /// Confirms a script with the host dialog. Fails CLOSED: with no dialog wired
    /// the action is refused rather than executed unprompted.
    /// </summary>
    private async Task<bool> ConfirmAsync(string title, string warning, string script) =>
        ShowScriptConfirmAsync != null && await ShowScriptConfirmAsync(title, warning, script);

    private async Task RunIndexActionAsync(ManagerNode? node, string title, string warning,
        Func<string, string, string, string> build)
    {
        if (node == null || _connInfo == null) return;
        var owner = node.Kind == NodeKind.IndexObj ? node.Parent : node;
        if (owner == null) return;

        var script = build(owner.Schema, owner.Name, node.Name);
        if (!await ConfirmAsync(title, warning, script))
            return;

        await RunSafeAsync(async ct =>
        {
            StatusMessage = $"{title}…";
            await _service.ExecuteRawScriptAsync(_connInfo, script, ct);
            StatusMessage = $"✓ {title} completed on {node.Name}.";
            await RefreshFolderAsync(node, NodeKind.IndexesFolder);
        });
    }

    private async Task UpdateStatsAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null) return;
        var owner = node.Kind == NodeKind.Stat ? node.Parent : node;
        if (owner == null) return;

        var script = node.Kind == NodeKind.Stat
            ? ManagerScriptBuilder.UpdateStatistics(owner.Schema, owner.Name, node.Name)
            : ManagerScriptBuilder.UpdateStatistics(owner.Schema, owner.Name);

        if (!await ConfirmAsync("Update statistics", "Refreshes query-optimizer statistics (fast, online).", script))
            return;

        await RunSafeAsync(async ct =>
        {
            StatusMessage = "Updating statistics…";
            await _service.ExecuteRawScriptAsync(_connInfo, script, ct);
            StatusMessage = $"✓ Statistics updated on {owner.Schema}.{owner.Name}.";
            await RefreshFolderAsync(node, NodeKind.StatsFolder);
        });
    }

    // ─── Database-level actions (root node) ──────────────────────────────────

    private async Task DatabasePropertiesAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || !node.CanDatabaseProps) return;
        if (ShowDbPropertiesAsync == null)
        {
            StatusMessage = "Properties dialog unavailable.";
            return;
        }
        await RunSafeAsync(async ct =>
        {
            StatusMessage = "Reading database properties…";
            var props = await _service.GetDatabasePropertiesAsync(_connInfo, ct);
            await ShowDbPropertiesAsync(props);
            StatusMessage = $"Database properties — {props.Name}, {props.DataSizeText} data.";
        });
    }

    private async Task ShrinkDatabaseAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || !node.CanShrink) return;
        var db = _connInfo.Database;
        var script = ManagerScriptBuilder.ShrinkDatabase(db);
        if (!await ConfirmAsync(
                "Shrink database",
                "Reclaims unused space. Shrinking causes index fragmentation — run it only after large deletions, then consider rebuilding indexes.",
                script))
            return;
        await RunSafeAsync(async ct =>
        {
            StatusMessage = $"Shrinking {db}…";
            await _service.ExecuteRawScriptAsync(_connInfo, script, ct);
            StatusMessage = $"✓ Shrink command issued on {db}.";
        });
    }

    private async Task RestoreDatabaseAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || !node.CanRestore) return;
        if (PickBackupFileAsync == null || ShowRestoreDialogAsync == null)
        {
            StatusMessage = "Restore needs a file picker and a dialog host; neither is attached.";
            return;
        }
        var backupPath = await PickBackupFileAsync();
        if (string.IsNullOrWhiteSpace(backupPath)) return;

        RestorePlan? plan = null;
        await RunSafeAsync(async ct =>
        {
            var file = Path.GetFileName(backupPath);
            StatusMessage = $"Reading backup sets from {file}…";
            var sets = await _service.ReadBackupSetsAsync(_connInfo, backupPath, ct);
            if (sets.Count == 0)
                throw new InvalidOperationException($"{file} contains no backup sets.");

            var (dataDir, logDir) = await _service.GetDefaultFileLocationsAsync(_connInfo, ct);
            var target = sets[^1].DatabaseName;
            var exists = await _service.DatabaseExistsAsync(_connInfo, target, ct);

            plan = await ShowRestoreDialogAsync(new RestoreDraft
            {
                BackupPath = backupPath,
                ConnectedDatabase = _connInfo.Database,
                Sets = sets,
                DataDirectory = dataDir,
                LogDirectory = logDir,
                TargetExists = exists,
                LoadFiles = position => _service.ReadBackupFilesAsync(_connInfo, backupPath, position, ct)
            });
            if (plan == null) StatusMessage = "Restore cancelled.";
        });
        if (plan == null) return;

        var script = ManagerScriptBuilder.RestoreDatabase(plan);
        var targetName = plan.TargetDatabase;
        var overwrite = plan.ReplaceExisting;
        if (!await ConfirmAsync(
                "Restore database",
                overwrite
                    ? $"⚠ [{targetName}] will be overwritten from {Path.GetFileName(plan.BackupPath)} " +
                      $"set #{plan.SetPosition}. Anything newer than that backup is lost and other connections are " +
                      $"disconnected while it runs. Take a fresh backup of {targetName} first if you may need to roll back."
                    : $"[{targetName}] will be created from {Path.GetFileName(plan.BackupPath)} " +
                      $"set #{plan.SetPosition}" +
                      (plan.RelocatedCount > 0 ? $", with {plan.RelocatedCount} file(s) relocated." : "."),
                script))
            return;

        await RunSafeAsync(async ct =>
        {
            var progress = new Progress<string>(m => StatusMessage = m);
            await _service.RestoreDatabaseAsync(_connInfo, plan, progress, ct);
            StatusMessage = plan.NoRecovery
                ? $"✓ {targetName} restored and left in a restoring state — the next backup can still be applied."
                : $"✓ {targetName} restored from set #{plan.SetPosition} — reopen the object tree to see its contents.";
            AppLog.Info(
                $"Restore of [{targetName}] from {plan.BackupPath} set #{plan.SetPosition} succeeded " +
                $"(overwrite={plan.ReplaceExisting}, relocated={plan.RelocatedCount}, stopAt={(plan.StopAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "none")}).");
        });
    }

    private async Task BackupDatabaseAsync(ManagerNode? node)
    {
        if (node == null || _connInfo == null || !node.CanBackup) return;
        if (ShowBackupDialogAsync == null)
        {
            StatusMessage = "Backup needs a dialog host, and this window has none attached.";
            return;
        }

        BackupRequest? request = null;
        await RunSafeAsync(async ct =>
        {
            var database = _connInfo.Database;
            StatusMessage = $"Reading backup defaults for [{database}] from {_connInfo.Server}…";
            var (backupDir, recoveryModel) = await _service.GetBackupDefaultsAsync(_connInfo, database, ct);
            request = await ShowBackupDialogAsync(new BackupDraft
            {
                Database = database,
                SuggestedFileName = ManagerScriptBuilder.SuggestedBackupFileName(database, DateTimeOffset.Now, false),
                DefaultBackupDirectory = backupDir,
                CanBackUpLog = !recoveryModel.Equals("SIMPLE", StringComparison.OrdinalIgnoreCase)
            });
            if (request == null) StatusMessage = "Backup cancelled.";
        });
        if (request == null) return;

        var script = ManagerScriptBuilder.BackupDatabase(request);
        if (!await ConfirmAsync(
                request.LogBackup ? "Back up transaction log" : "Back up database",
                $"SQL Server will write {request.FilePath} on the server host ({_connInfo.Server}), not on this PC. " +
                (request.OverwriteMedia
                    ? "The existing media file will be reformatted, discarding the backup sets it holds."
                    : "A new backup set is appended, so earlier sets in that file stay restorable.") +
                (request.CopyOnly ? " This is a copy-only backup and does not affect the normal recovery chain." : ""),
                script))
            return;

        var target = request;
        await RunSafeAsync(async ct =>
        {
            var progress = new Progress<string>(m => StatusMessage = m);
            StatusMessage = $"BACKUP {(target.LogBackup ? "LOG" : "DATABASE")} [{target.Database}] accepted by {_connInfo.Server}.";
            var result = await _backupService.BackupAsync(_connInfo, target, progress, ct);
            StatusMessage = result;
            AppLog.Info($"Backup of [{target.Database}] to {target.FilePath}: {result}");
        });
    }

    private async Task RefreshNodeCommandAsync(ManagerNode? node)
    {
        if (node == null) return;
        if (node.Kind is NodeKind.Table or NodeKind.View)
        {
            foreach (var f in node.Children)
                await ReloadFolderAsync(f);
            return;
        }
        await ReloadFolderAsync(node);
    }

    private async Task ReloadFolderAsync(ManagerNode folder)
    {
        if (folder.Loader != null)
        {
            folder.HasLoaded = false;
            await folder.RunLoaderNow();
            return;
        }
        await ReloadObjectFolderAsync(folder);
    }

    /// <summary>The object folders are filled eagerly when the database node loads and
    /// carry no Loader of their own, so refreshing one has to re-query the catalogue and
    /// swap its children in place — otherwise a created or dropped object stays invisible.</summary>
    private async Task ReloadObjectFolderAsync(ManagerNode folder)
    {
        if (_connInfo == null) return;
        var type = folder.Kind switch
        {
            NodeKind.TablesFolder => DbObjectType.Table,
            NodeKind.ViewsFolder => DbObjectType.View,
            NodeKind.ProcsFolder => DbObjectType.StoredProcedure,
            NodeKind.FunctionsFolder => DbObjectType.Function,
            NodeKind.TriggersFolder => DbObjectType.Trigger,
            _ => (DbObjectType?)null,
        };
        if (type == null) return;

        folder.IsLoading = true;
        try
        {
            var items = await _service.GetObjectsAsync(_connInfo, type.Value,
                CancellationToken.None, BuildExplorerFilter());
            folder.Children.Clear();
            foreach (var it in items)
            {
                var n = MakeObjectNode(it);
                n.Parent = folder;
                folder.Children.Add(n);
            }
            folder.Label = $"{folder.Label.Split(" (")[0].TrimEnd()} ({items.Count})";
        }
        finally
        {
            folder.IsLoading = false;
        }
    }

    /// <summary>Re-runs the loader of the sibling folder of the given kind (from a leaf node).</summary>
    private async Task RefreshFolderAsync(ManagerNode node, NodeKind folderKind)
    {
        var owner = node.Kind is NodeKind.Table or NodeKind.View ? node : node.Parent;
        if (owner == null) return;
        var folder = owner.Children.FirstOrDefault(c => c.Kind == folderKind);
        if (folder != null)
            await ReloadFolderAsync(folder);
    }

    private async Task ShowScriptInDefinitionTabAsync(string schema, string name, DbObjectType type, string script)
    {
        _pendingDefinition = script;
        SelectedObject = new DbObjectInfo { Schema = schema, Name = name, ObjectType = type };
        await SwitchTabAsync(ManagerTab.Definition);
    }

    // -------------------------------------------------------------------------
    // Object selection & tab loading
    // -------------------------------------------------------------------------
    partial void OnSelectedObjectChanged(DbObjectInfo? value)
    {
        ((AsyncRelayCommand)ApplyFiltersCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ExecuteProcCommand).NotifyCanExecuteChanged();
        if (value == null) return;
        var preferredTab = value.ObjectType switch
        {
            DbObjectType.Table or DbObjectType.View => ManagerTab.Data,
            DbObjectType.StoredProcedure            => ManagerTab.Execute,
            _                                       => ManagerTab.Definition
        };
        _ = SwitchTabAsync(preferredTab);
    }

    private async Task SwitchTabAsync(ManagerTab tab)
    {
        ActiveTab        = tab;
        IsTabData        = tab == ManagerTab.Data;
        IsTabStructure   = tab == ManagerTab.Structure;
        IsTabDefinition  = tab == ManagerTab.Definition;
        IsTabExecute     = tab == ManagerTab.Execute;
        ShowExecuteTab   = SelectedObject?.ObjectType == DbObjectType.StoredProcedure;

        if (SelectedObject == null || !IsConnected || SelectedConnection == null) return;
        var info = CurrentConnection();
        var obj  = SelectedObject;

        switch (tab)
        {
            case ManagerTab.Data:
                await LoadTableDataAsync(info, obj);
                break;
            case ManagerTab.Structure:
                await LoadStructureAsync(info, obj);
                break;
            case ManagerTab.Definition:
                await LoadDefinitionAsync(info, obj);
                break;
            case ManagerTab.Execute:
                await LoadProcParamsAsync(info, obj);
                break;
        }
    }

    // -------------------------------------------------------------------------
    private string? _currentTableKey;

    private async Task LoadTableDataAsync(ConnectionInfo info, DbObjectInfo obj)
    {
        await RunSafeAsync(async ct =>
        {
            IsLoadingData = true;
            HasTableData  = false;
            TableRows.Clear();

            var tableKey = $"{obj.Schema}.{obj.Name}";
            if (!string.Equals(_currentTableKey, tableKey, StringComparison.OrdinalIgnoreCase))
            {
                _currentTableKey = tableKey;
                TableFilters.Clear();
                TableColumnNames.Clear();
            }

            StatusMessage = $"Loading data from {obj.DisplayName}…";

            var (cols, rows) = await _service.GetTableDataAsync(
                info, obj.Schema, obj.Name, TableFilters, TopNRows, ct);

            if (!TableColumnNames.SequenceEqual(cols))
            {
                var savedSelections = TableFilters.Select(f => f.ColumnName).ToList();
                TableColumnNames.Clear();
                foreach (var c in cols) TableColumnNames.Add(c);
                for (int i = 0; i < TableFilters.Count && i < savedSelections.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(savedSelections[i]) && cols.Contains(savedSelections[i]))
                        TableFilters[i].ColumnName = savedSelections[i];
                }
            }

            foreach (var r in rows) TableRows.Add(r);

            // Discard any pending in-memory edits for rows that no longer exist
            _pendingRowEdits.Clear();
            _originalRowValues.Clear();
            HasPendingEdits = false;
            ((AsyncRelayCommand)SaveTableChangesCommand).NotifyCanExecuteChanged();

            LoadedRowCount = TableRows.Count;
            HasTableData   = TableRows.Count > 0;

            if (obj.ObjectType == DbObjectType.Table)
            {
                try
                {
                    TotalTableRowCount = await _service.GetRowCountAsync(info, obj.Schema, obj.Name, ct);
                    HasRowCountInfo = true;
                    DataStatusText = $"{LoadedRowCount:N0} row{(LoadedRowCount == 1 ? "" : "s")} displayed (out of {TotalTableRowCount:N0} total) — {obj.DisplayName}";
                }
                catch
                {
                    TotalTableRowCount = LoadedRowCount;
                    HasRowCountInfo = true;
                    DataStatusText = $"{LoadedRowCount:N0} row{(LoadedRowCount == 1 ? "" : "s")} — {obj.DisplayName}";
                }
            }
            else
            {
                TotalTableRowCount = LoadedRowCount;
                HasRowCountInfo = true;
                DataStatusText = $"{LoadedRowCount:N0} row{(LoadedRowCount == 1 ? "" : "s")} — {obj.DisplayName}";
            }

            StatusMessage = DataStatusText;
            IsLoadingData = false;
        });
        IsLoadingData = false;
    }

    // -------------------------------------------------------------------------
    // Row editing / save / delete (Data tab)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by the view when a cell edit begins, before the new value is
    /// committed to the row, so Save Changes can build a correct WHERE clause.
    /// </summary>
    public void SnapshotRow(Dictionary<string, object?> row)
    {
        if (!_originalRowValues.ContainsKey(row))
            _originalRowValues[row] = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns the pre-edit value captured by <see cref="SnapshotRow"/>.</summary>
    public object? GetOriginalCellValue(Dictionary<string, object?> row, string columnName)
    {
        if (_originalRowValues.TryGetValue(row, out var snap) && snap.TryGetValue(columnName, out var v))
            return v;
        return row.TryGetValue(columnName, out var cur) ? cur : null;
    }

    /// <summary>
    /// Called by the view (CellEditEnding) after a cell edit is committed so the
    /// change can be flushed to the database when the user clicks Save Changes.
    /// </summary>
    public void CaptureCellEdit(Dictionary<string, object?> row, string columnName, object? newValue)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return;
        if (!_pendingRowEdits.TryGetValue(row, out var changes))
        {
            changes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            _pendingRowEdits[row] = changes;
        }
        // Fall back to a snapshot now if BeginningEdit wasn't observed
        SnapshotRow(row);
        changes[columnName] = newValue is string s && string.Equals(s.Trim(), "NULL", StringComparison.OrdinalIgnoreCase)
            ? null
            : newValue;

        HasPendingEdits = true;
        ((AsyncRelayCommand)SaveTableChangesCommand).NotifyCanExecuteChanged();
        StatusMessage = "Pending change — click 💾 Save Changes to write to the database.";
    }

    private async Task SaveTableChangesAsync()
    {
        if (SelectedConnection == null || SelectedObject == null)
        {
            StatusMessage = "Connect and select a table first.";
            return;
        }
        if (!_pendingRowEdits.Any())
        {
            StatusMessage = "No pending changes.";
            return;
        }

        var info = CurrentConnection();
        var obj  = SelectedObject;

        await RunSafeAsync(async ct =>
        {
            var cols   = await _service.GetTableColumnsAsync(info, obj.Schema, obj.Name, ct);
            var pkCols = cols.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

            if (pkCols.Count == 0)
            {
                StatusMessage = "⚠ Cannot save — table has no primary key.";
                return;
            }

            int saved = 0;
            var pendingCells = _pendingRowEdits.Sum(e => e.Value.Count);
            var changePreview = string.Join("\n", _pendingRowEdits.Select(e =>
                $"-- {obj.DisplayName}: {string.Join(", ", e.Value.Keys)}"));
            if (!await ConfirmAsync("Save grid changes",
                    $"Writes {pendingCells} cell change(s) across {_pendingRowEdits.Count} row(s) to {obj.DisplayName}," +
                    " matched on the original primary keys. This cannot be undone.",
                    changePreview))
                return;

            foreach (var (row, changes) in _pendingRowEdits.ToList())
            {
                if (!_originalRowValues.TryGetValue(row, out var original)) continue;

                // Validate: all PK values must be present
                var pkValues = new Dictionary<string, object?>();
                var missingPk = false;
                foreach (var pk in pkCols)
                {
                    if (!original.TryGetValue(pk, out var v) || v == null || v is DBNull)
                    { missingPk = true; break; }
                    pkValues[pk] = ConvertForDb(cols, pk, v);
                }
                if (missingPk)
                {
                    StatusMessage = "⚠ Skipped a row — its primary key value is null.";
                    continue;
                }

                // Only columns that actually changed and are not PK/identity
                var setCols = changes
                    .Where(kv => !pkCols.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                    .Where(kv => !ValuesEqual(
                        original.TryGetValue(kv.Key, out var ov) ? ov : null,
                        kv.Value))
                    .ToDictionary(kv => kv.Key, kv => ConvertForDb(cols, kv.Key, kv.Value), StringComparer.OrdinalIgnoreCase);

                if (setCols.Count == 0) continue;

                await _service.UpdateRowAsync(info, obj.Schema, obj.Name, pkValues, setCols, ct);
                saved++;
            }

            _pendingRowEdits.Clear();
            _originalRowValues.Clear();
            HasPendingEdits = false;
            ((AsyncRelayCommand)SaveTableChangesCommand).NotifyCanExecuteChanged();

            StatusMessage = $"✓ {saved} row(s) saved to {obj.DisplayName}. Reloading…";
            await LoadTableDataAsync(info, obj);
        });
    }

    private async Task DeleteSelectedRowAsync()
    {
        if (SelectedConnection == null || SelectedObject == null || SelectedRow == null)
            return;
        var info = CurrentConnection();
        var obj  = SelectedObject;
        var row  = SelectedRow;

        await RunSafeAsync(async ct =>
        {
            var cols   = await _service.GetTableColumnsAsync(info, obj.Schema, obj.Name, ct);
            var pkCols = cols.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

            if (pkCols.Count == 0)
            {
                StatusMessage = "⚠ Cannot delete — table has no primary key.";
                return;
            }

            var pkValues = new Dictionary<string, object?>();
            foreach (var pk in pkCols)
            {
                if (!row.TryGetValue(pk, out var v) || v == null || v is DBNull)
                {
                    StatusMessage = "⚠ Cannot delete — primary key value is null for the selected row.";
                    return;
                }
                pkValues[pk] = ConvertForDb(cols, pk, v);
            }

            var where = string.Join(" AND ", pkValues.Select(kv =>
                $"[{kv.Key}] = {(kv.Value is string s ? "N'" + s.Replace("'", "''") + "'" : kv.Value)}"));
            if (!await ConfirmAsync("Delete row",
                    $"Deletes 1 row from {obj.DisplayName}. This cannot be undone.",
                    $"DELETE FROM [{obj.Schema}].[{obj.Name}] WHERE {where};"))
                return;

            await _service.DeleteRowAsync(info, obj.Schema, obj.Name, pkValues, ct);            _pendingRowEdits.Remove(row);
            _originalRowValues.Remove(row);

            StatusMessage = $"✓ Row deleted from {obj.DisplayName}. Reloading…";
            await LoadTableDataAsync(info, obj);
        });
    }

    private async Task ApplyFiltersAsync()
    {
        if (SelectedConnection == null)
        {
            StatusMessage = "Please select a connection and connect first.";
            return;
        }
        if (SelectedObject == null)
        {
            StatusMessage = "Please select a table or view from the left panel first.";
            return;
        }

        // Remove empty filter rows before querying
        var emptyFilters = TableFilters.Where(f => string.IsNullOrWhiteSpace(f.ColumnName)).ToList();
        foreach (var ef in emptyFilters)
            TableFilters.Remove(ef);

        await LoadTableDataAsync(CurrentConnection(), SelectedObject);
    }

    private void AddFilter()
    {
        var col = TableColumnNames.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;
        TableFilters.Add(new TableFilter { ColumnName = col });
    }

    private void RemoveFilter(TableFilter? filter)
    {
        if (filter != null) TableFilters.Remove(filter);
    }

    private void ClearFilters()
    {
        TableFilters.Clear();
        _ = ApplyFiltersAsync();
    }

    /// <summary>
    /// Compares an original cell value with an edited one (treating null/DBNull
    /// and the "(NULL)" display placeholder as equal) so unchanged cells are skipped.
    /// </summary>
    private static bool ValuesEqual(object? original, object? edited)
    {
        if (original is DBNull) original = null;
        if (edited   is DBNull) edited   = null;
        var origText = original switch
        {
            null   => string.Empty,
            string => (string)original,
            _      => original.ToString() ?? string.Empty
        };
        var editText = edited switch
        {
            null        => string.Empty,
            string es   => es,
            _           => edited.ToString() ?? string.Empty
        };
        if (string.Equals(editText, "(NULL)", StringComparison.OrdinalIgnoreCase)) editText = string.Empty;
        if (string.Equals(origText, "(NULL)", StringComparison.OrdinalIgnoreCase)) origText = string.Empty;
        return string.Equals(origText.Trim(), editText.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts a user-entered cell string to the appropriate CLR type for the
    /// column so SQL Server receives typed parameters (falls back to the raw string).
    /// </summary>
    private static object? ConvertForDb(List<TableColumn> cols, string columnName, object? value)
    {
        if (value is null or DBNull) return DBNull.Value;
        if (value is not string s) return value;
        if (string.Equals(s.Trim(), "NULL", StringComparison.OrdinalIgnoreCase)) return DBNull.Value;

        var col = cols.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
        var t   = col?.DataType.ToLowerInvariant();
        try
        {
            return t switch
            {
                "int" or "smallint"                                    => int.Parse(s),
                "bigint"                                               => long.Parse(s),
                "tinyint"                                              => byte.Parse(s),
                "bit"                                                  => s is "1" or "true" or "TRUE" or "True",
                "decimal" or "numeric" or "money" or "smallmoney"      => decimal.Parse(s),
                "float"                                                => double.Parse(s),
                "real"                                                 => float.Parse(s),
                "uniqueidentifier"                                     => Guid.Parse(s),
                "datetime" or "datetime2" or "smalldatetime" or "date" => DateTime.Parse(s),
                _ => s
            };
        }
        catch
        {
            return s; // let SQL Server surface a descriptive conversion error
        }
    }

    // -------------------------------------------------------------------------
    // Structure tab
    // -------------------------------------------------------------------------
    private async Task LoadStructureAsync(ConnectionInfo info, DbObjectInfo obj)
    {
        await RunSafeAsync(async ct =>
        {
            StructureColumns.Clear();
            HasStructure = false;
            StatusMessage = $"Loading structure of {obj.DisplayName}…";

            var cols = await _service.GetTableColumnsAsync(info, obj.Schema, obj.Name, ct);
            foreach (var c in cols) StructureColumns.Add(c);
            HasStructure = StructureColumns.Count > 0;
            StatusMessage = $"{StructureColumns.Count} column(s) — {obj.DisplayName}";
        });
    }

    // -------------------------------------------------------------------------
    // Definition tab (View / Modify SQL)
    // -------------------------------------------------------------------------
    private async Task LoadDefinitionAsync(ConnectionInfo info, DbObjectInfo obj)
    {
        await RunSafeAsync(async ct =>
        {
            HasDefinition    = false;
            ObjectDefinition = string.Empty;
            DefinitionSaveStatus = string.Empty;

            if (_pendingDefinition != null)
            {
                ObjectDefinition = _pendingDefinition;
                HasDefinition    = true;
                _pendingDefinition = null;
                StatusMessage = "Script ready — edit and click Save / Execute to apply.";
                return;
            }

            StatusMessage    = $"Loading definition of {obj.DisplayName}…";

            var def = await _service.GetObjectDefinitionAsync(info, obj.Schema, obj.Name, obj.ObjectType, ct);
            ObjectDefinition = def;
            HasDefinition    = !string.IsNullOrWhiteSpace(def);
            StatusMessage    = $"Definition loaded — {obj.DisplayName}";
        });
    }

    private async Task CopyDefinitionAsync()
    {
        if (CopyToClipboardAsync != null && !string.IsNullOrWhiteSpace(ObjectDefinition))
            await CopyToClipboardAsync(ObjectDefinition);
    }

    private async Task SaveDefinitionAsync()
    {
        if (SelectedConnection == null || SelectedObject == null || string.IsNullOrWhiteSpace(ObjectDefinition)) return;
        var info = CurrentConnection();
        var obj = SelectedObject;

        await RunSafeAsync(async ct =>
        {
            IsSavingDefinition = true;
            DefinitionSaveStatus = "Executing ALTER/CREATE script…";
            StatusMessage = $"Applying modified definition for {obj.DisplayName}…";

            await _service.ExecuteRawScriptAsync(info, ObjectDefinition, ct);

            DefinitionSaveStatus = $"✓ Updated successfully at {DateTime.Now:HH:mm:ss}";
            StatusMessage = $"Saved {obj.DisplayName} successfully.";
        });
        IsSavingDefinition = false;
    }

    // -------------------------------------------------------------------------
    // Execute tab (Stored Procedure)
    // -------------------------------------------------------------------------
    private async Task LoadProcParamsAsync(ConnectionInfo info, DbObjectInfo obj)
    {
        if (obj.ObjectType != DbObjectType.StoredProcedure) return;
        await RunSafeAsync(async ct =>
        {
            ProcParams.Clear();
            ExecResultRows.Clear();
            ExecResultColumns.Clear();
            ExecOutputText = string.Empty;
            HasExecResults = false;
            StatusMessage  = $"Loading parameters of {obj.DisplayName}…";

            var parms = await _service.GetProcedureParamsAsync(info, obj.Schema, obj.Name, ct);
            foreach (var p in parms) ProcParams.Add(p);
            StatusMessage = parms.Count > 0
                ? $"{parms.Count} parameter(s) — fill values and click Execute."
                : "No parameters — click Execute to run.";
        });
    }

    private async Task ExecuteProcAsync()
    {
        if (SelectedObject == null || SelectedConnection == null) return;
        var obj  = SelectedObject;
        var info = CurrentConnection();

        await RunSafeAsync(async ct =>
        {
            IsExecuting = true;
            ExecResultRows.Clear();
            ExecResultColumns.Clear();
            ExecOutputText = string.Empty;
            HasExecResults = false;
            StatusMessage  = $"Executing {obj.DisplayName}…";

            var (cols, rows, outputs, messages) =
                await _service.ExecuteProcedureAsync(info, obj.Schema, obj.Name, ProcParams, ct);

            foreach (var c in cols) ExecResultColumns.Add(c);
            foreach (var r in rows) ExecResultRows.Add(r);
            HasExecResults = ExecResultRows.Count > 0 || outputs.Count > 0;

            var sb = new System.Text.StringBuilder();
            if (messages.Length > 0) sb.AppendLine("--- Messages ---\n" + messages);
            if (outputs.Count > 0)
            {
                sb.AppendLine("--- Output Parameters ---");
                foreach (var kv in outputs)
                    sb.AppendLine($"  {kv.Key} = {kv.Value ?? "NULL"}");
            }
            ExecOutputText = sb.ToString().Trim();
            StatusMessage = $"Procedure executed — {rows.Count} row(s) returned.";
            IsExecuting = false;
        });
        IsExecuting = false;
    }

    // -------------------------------------------------------------------------
    // Search
    // -------------------------------------------------------------------------
    private async Task SearchAsync()
    {
        if (SelectedConnection == null || string.IsNullOrWhiteSpace(SearchText)) return;
        await RunSafeAsync(async ct =>
        {
            IsLoadingTree = true;
            StatusMessage = $"Searching for \"{SearchText}\"…";
            var info    = CurrentConnection();
            var results = await _service.SearchObjectsAsync(info, SearchText, ct);

            ManagerTreeRoots.Clear();
            if (results.Count == 0)
            {
                StatusMessage = "No objects matched the search.";
                IsLoadingTree = false;
                return;
            }

            _connInfo = info;
            var root = new ManagerNode
            {
                Kind = NodeKind.SearchFolder,
                Label = $"Search: \"{SearchText}\"",
                Icon = "🔍",
                IsExpanded = true
            };
            foreach (var g in results.GroupBy(r => r.ObjectType))
            {
                var grp = new ManagerNode { Kind = NodeKind.SearchFolder, Label = $"{g.Key} ({g.Count()})", Icon = "🗂" };
                foreach (var it in g)
                {
                    var n = MakeObjectNode(it);
                    n.Parent = grp;
                    grp.Children.Add(n);
                }
                root.Children.Add(grp);
            }
            ManagerTreeRoots.Add(root);
            StatusMessage = $"Found {results.Count} object(s) matching \"{SearchText}\".";
            IsLoadingTree = false;
        });
        IsLoadingTree = false;
    }

    private async Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        if (IsConnected && SelectedConnection != null)
        {
            await RunSafeAsync(async ct =>
            {
                IsLoadingTree = true;
                var info = CurrentConnection();
                await LoadObjectTreeAsync(info, ct);
                StatusMessage = "Search cleared.";
            });
            IsLoadingTree = false;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private async Task RunSafeAsync(Func<CancellationToken, Task> action)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ShowError  = false;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            await action(ct);
        }
        catch (OperationCanceledException) { }
        // This token is only ever cancelled when a newer operation supersedes this
        // one — typing in the filter box mid-load, for example. Its half-done failure
        // must not land on top of the newer operation's status.
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            AppLog.Info($"[Manager] Superseded operation stopped: {ex.Message}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ShowError    = true;
            StatusMessage = "Error — see details above.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Notify CanExecute when key properties change
    partial void OnSelectedConnectionChanged(SavedConnection? value)
    {
        ((AsyncRelayCommand)ConnectCommand).NotifyCanExecuteChanged();
    }

    partial void OnIsConnectedChanged(bool value)
    {
        ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ApplyFiltersCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ClearSearchCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ExecuteProcCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SaveDefinitionCommand).NotifyCanExecuteChanged();
    }

    partial void OnIsExecutingChanged(bool value)
    {
        ((AsyncRelayCommand)ExecuteProcCommand).NotifyCanExecuteChanged();
    }

    partial void OnHasDefinitionChanged(bool value)
    {
        ((AsyncRelayCommand)CopyDefinitionCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SaveDefinitionCommand).NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingDataChanged(bool value)
    {
        ((AsyncRelayCommand)ApplyFiltersCommand).NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
    }
}
