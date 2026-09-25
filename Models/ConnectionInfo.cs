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

    private string SecurityPart =>
        $"Encrypt={(EncryptConnection ? "True" : "False")};TrustServerCertificate={TrustServerCertificate};";

    /// <summary>Full connection string used for DacFx schema operations.</summary>
    public string ConnectionString => UseWindowsAuth
        ? $"Server={Server};Database={Database};Integrated Security=True;{SecurityPart}"
        : $"Server={Server};Database={Database};User Id={Username};Password={Password};{SecurityPart}";

    /// <summary>
    /// Connection string with a short Connect Timeout for quick reachability tests.
    /// Uses Microsoft.Data.SqlClient format.
    /// </summary>
    public string BuildTestConnectionString(int timeoutSeconds = 8) => UseWindowsAuth
        ? $"Server={Server};Database={Database};Integrated Security=True;{SecurityPart}Connect Timeout={timeoutSeconds};"
        : $"Server={Server};Database={Database};User Id={Username};Password={Password};{SecurityPart}Connect Timeout={timeoutSeconds};";
}
