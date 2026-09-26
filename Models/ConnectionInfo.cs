namespace SchemaCompare.Models;

public class ConnectionInfo
{
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public bool UseWindowsAuth { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Force TLS on the wire (Encrypt=True). Off by default so local
    /// instances without a server certificate keep connecting.</summary>
    public bool EncryptConnection { get; set; }

    /// <summary>Accept a server certificate that no trusted CA signed. Turn this
    /// off for anything that is not a lab or local server.</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>Entra ID authentication method (a Microsoft.Data.SqlClient
    /// <c>Authentication=</c> value such as <c>ActiveDirectoryInteractive</c>).
    /// Empty keeps the classic Windows / SQL Server paths.</summary>
    public string Authentication { get; set; } = string.Empty;

    /// <summary>True for any Entra ID flow, false for Windows and SQL auth.</summary>
    public bool UsesEntraAuth => !string.IsNullOrWhiteSpace(Authentication);

    private string AuthPart => UsesEntraAuth
        ? AuthMethod.MethodNeedsCredentials(Authentication)
            ? $"Authentication={Authentication};User Id={Username};Password={Password};"
            : $"Authentication={Authentication};"
        : UseWindowsAuth
            ? "Integrated Security=True;"
            : $"User Id={Username};Password={Password};";

    private string SecurityPart =>
        $"Encrypt={(AuthMethod.MethodForcesEncryption(Authentication) || EncryptConnection ? "True" : "False")};" +
        $"TrustServerCertificate={TrustServerCertificate};";

    /// <summary>Full connection string used for DacFx schema operations.</summary>
    public string ConnectionString => $"Server={Server};Database={Database};{AuthPart}{SecurityPart}";

    /// <summary>
    /// Connection string with a short Connect Timeout for quick reachability tests.
    /// Uses Microsoft.Data.SqlClient format.
    /// </summary>
    public string BuildTestConnectionString(int timeoutSeconds = 8) =>
        $"Server={Server};Database={Database};{AuthPart}{SecurityPart}Connect Timeout={timeoutSeconds};";

    /// <summary>The same connection without a database, for server-level work such as
    /// listing databases, logins or Agent jobs.</summary>
    public string ServerConnectionString(int timeoutSeconds = 8) =>
        $"Server={Server};Database=master;{AuthPart}{SecurityPart}Connect Timeout={timeoutSeconds};";

    /// <summary>The same login pointed at another database. Used when the explorer
    /// switches context to a sibling database — credentials are never re-typed and
    /// the saved profile is never rewritten.</summary>
    public ConnectionInfo ForDatabase(string database) => new()
    {
        Server = Server,
        Database = database,
        UseWindowsAuth = UseWindowsAuth,
        Username = Username,
        Password = Password,
        EncryptConnection = EncryptConnection,
        TrustServerCertificate = TrustServerCertificate,
        Authentication = Authentication
    };

    /// <summary>A connection string safe to write to a log or an audit line: it names
    /// the server, the database and the flow, never a user name or a secret.</summary>
    public string SafeForLog => UsesEntraAuth
        ? $"{Server}/{Database} via {Authentication}"
        : $"{Server}/{Database} via {(UseWindowsAuth ? "Windows auth" : "SQL auth")}";
}
