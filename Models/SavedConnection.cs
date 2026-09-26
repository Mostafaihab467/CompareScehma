using CommunityToolkit.Mvvm.ComponentModel;

namespace SchemaCompare.Models;

/// <summary>
/// A saved DB credential profile. Password is held in plain text only in memory;
/// it is DPAPI-encrypted when persisted to disk by <see cref="Services.SavedConnectionsService"/>.
/// </summary>
public partial class SavedConnection : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _server = string.Empty;
    [ObservableProperty] private string _database = string.Empty;
    [ObservableProperty] private bool _useWindowsAuth = true;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;

    /// <summary>Force TLS (Encrypt=True) when connecting with this profile.</summary>
    [ObservableProperty] private bool _encryptConnection;

    /// <summary>Accept an untrusted server certificate. On by default for local/lab servers.</summary>
    [ObservableProperty] private bool _trustServerCertificate = true;

    /// <summary>Entra ID authentication method name; empty means Windows or SQL auth.</summary>
    [ObservableProperty] private string _authentication = string.Empty;

    /// <summary>The dropdown selection this profile represents, so loading a profile
    /// restores the authentication mode and not just the user name.</summary>
    public AuthMethod Auth => AuthMethod.For(Authentication, UseWindowsAuth);

    partial void OnAuthenticationChanged(string value) => OnPropertyChanged(nameof(Auth));

    partial void OnUseWindowsAuthChanged(bool value)
    {
        OnPropertyChanged(nameof(Auth));
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Short label shown in the dropdowns.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? BuildDefaultName()
        : Name;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnServerChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnDatabaseChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnUsernameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    public string BuildDefaultName()
    {
        var baseName = $"{Server}/{Database}".Trim('/');
        if (string.IsNullOrWhiteSpace(baseName))
            return "Unnamed connection";
        var suffix = Auth.IsWindows ? "WinAuth"
            : Auth.AuthenticationMethod.Length > 0 ? "Entra"
            : Username;
        return $"{baseName} ({suffix})";
    }

    /// <summary>True when server+database (+username for SQL auth) match.</summary>
    public bool Matches(string server, string database, bool useWindowsAuth, string username,
                        string? authentication = null) =>
        string.Equals(Server?.Trim(), server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Database?.Trim(), database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        UseWindowsAuth == useWindowsAuth &&
        string.Equals(Authentication ?? "", authentication ?? "", StringComparison.OrdinalIgnoreCase) &&
        (useWindowsAuth || string.Equals(Username?.Trim(), username?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Converts this saved profile to a <see cref="ConnectionInfo"/> for live queries.</summary>
    public ConnectionInfo ToConnectionInfo() => new()
    {
        Server         = Server,
        Database       = Database,
        UseWindowsAuth = UseWindowsAuth,
        Username       = Username,
        Password       = Password,
        EncryptConnection = EncryptConnection,
        TrustServerCertificate = TrustServerCertificate,
        Authentication = Authentication
    };
}
