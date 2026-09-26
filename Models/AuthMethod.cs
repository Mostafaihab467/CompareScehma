namespace SchemaCompare.Models;

/// <summary>
/// One option of the authentication dropdown. <see cref="AuthenticationMethod"/> is the
/// value Microsoft.Data.SqlClient expects for its <c>Authentication=</c> keyword, empty for
/// the classic Windows / SQL Server paths so older code keeps working untouched.
/// </summary>
public sealed record AuthMethod(
    string Display,
    string AuthenticationMethod,
    bool IsWindows,
    bool NeedsCredentials,
    string CredentialHint)
{
    /// <summary>Compact label for log and audit lines; names no user and no secret.</summary>
    public string Short => IsWindows ? "Windows auth"
        : AuthenticationMethod.Length == 0 ? "SQL auth"
        : "Entra " + AuthenticationMethod.Replace("ActiveDirectory", "");
    public static readonly AuthMethod Windows = new(
        "Windows Authentication", "", true, false, "");

    public static readonly AuthMethod SqlServer = new(
        "SQL Server Authentication", "", false, true, "Login and password");

    public static readonly AuthMethod EntraDefault = new(
        "Entra ID — Default (browser, cached account, managed identity)",
        "ActiveDirectoryDefault", false, false, "");

    public static readonly AuthMethod EntraInteractive = new(
        "Entra ID — Interactive (sign-in prompt, supports MFA)",
        "ActiveDirectoryInteractive", false, false, "");

    public static readonly AuthMethod EntraPassword = new(
        "Entra ID — User name and password",
        "ActiveDirectoryPassword", false, true, "user@tenant.onmicrosoft.com");

    public static readonly AuthMethod EntraIntegrated = new(
        "Entra ID — Windows integrated",
        "ActiveDirectoryIntegrated", false, false, "");

    public static readonly AuthMethod EntraServicePrincipal = new(
        "Entra ID — Application (client id + client secret)",
        "ActiveDirectoryServicePrincipal", false, true, "app-client-id");

    public static readonly AuthMethod EntraDeviceCode = new(
        "Entra ID — Device code flow",
        "ActiveDirectoryDeviceCodeFlow", false, false, "");

    public static IReadOnlyList<AuthMethod> All { get; } =
        [Windows, SqlServer, EntraDefault, EntraInteractive, EntraPassword, EntraIntegrated,
         EntraServicePrincipal, EntraDeviceCode];

    /// <summary>The dropdown entry for a saved profile. Unknown Entra method names are
    /// kept verbatim rather than silently downgraded to Windows auth.</summary>
    public static AuthMethod For(string? authenticationMethod, bool useWindowsAuth)
    {
        if (string.IsNullOrWhiteSpace(authenticationMethod))
            return useWindowsAuth ? Windows : SqlServer;
        return All.FirstOrDefault(a =>
                   string.Equals(a.AuthenticationMethod, authenticationMethod, StringComparison.OrdinalIgnoreCase))
               ?? new AuthMethod($"Entra ID — {authenticationMethod}", authenticationMethod,
                    false, true, "client id / user name");
    }

    /// <summary>True when the Entra flow authenticates with a stored secret (password or
    /// client secret) and therefore needs the credential boxes.</summary>
    public static bool MethodNeedsCredentials(string? authenticationMethod) =>
        For(authenticationMethod, false).NeedsCredentials;

    /// <summary>Entra always talks TLS to a public endpoint, so the driver must not be
    /// told to dial encryption down.</summary>
    public static bool MethodForcesEncryption(string? authenticationMethod) =>
        !string.IsNullOrWhiteSpace(authenticationMethod);

    public override string ToString() => Display;
}
