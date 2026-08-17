namespace SchemaCompare.Models;

public class ConnectionInfo
{
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public bool UseWindowsAuth { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Full connection string used for DacFx schema operations.</summary>
    public string ConnectionString => UseWindowsAuth
        ? $"Server={Server};Database={Database};Integrated Security=True;TrustServerCertificate=True;"
        : $"Server={Server};Database={Database};User Id={Username};Password={Password};TrustServerCertificate=True;";

    /// <summary>
    /// Connection string with a short Connect Timeout for quick reachability tests.
    /// Uses Microsoft.Data.SqlClient format.
    /// </summary>
    public string BuildTestConnectionString(int timeoutSeconds = 8) => UseWindowsAuth
        ? $"Server={Server};Database={Database};Integrated Security=True;TrustServerCertificate=True;Connect Timeout={timeoutSeconds};Encrypt=Optional;"
        : $"Server={Server};Database={Database};User Id={Username};Password={Password};TrustServerCertificate=True;Connect Timeout={timeoutSeconds};Encrypt=Optional;";
}
