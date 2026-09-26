namespace SchemaCompare.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Rows behind the server-level Object Explorer nodes (DbManagerService's
// "server browsing" region). Everything here is read from the instance the
// explorer is connected to, over a master-database connection.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>What the instance reports about itself, shown on the server node.</summary>
public sealed record ServerOverview(string ServerName, string Version, string Edition, string Collation)
{
    public string ShortLabel => string.IsNullOrWhiteSpace(Version)
        ? ServerName
        : $"{ServerName} — {Version}";
}

/// <summary>One database on the instance. <see cref="SizeMb"/> is data + log.</summary>
public sealed record ServerDatabaseRow(string Name, string State, long SizeMb)
{
    public bool IsOnline => State.Equals("ONLINE", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A server login or server role from sys.server_principals.</summary>
public sealed record ServerPrincipalRow(string Name, string TypeDesc, string DefaultDatabase, bool IsDisabled)
{
    public bool IsWindowsLogin => TypeDesc.Contains("WINDOWS_GROUP") || TypeDesc.Contains("WINDOWS_LOGIN");
    public string Label => IsDisabled ? Name + " (disabled)" : Name;
}

/// <summary>A linked (distributed-query) server. server_id 0 is the instance itself,
/// which sys.servers names differently across versions — so it is recognised by id.</summary>
public sealed record LinkedServerRow(string Name, string Product, string DataSource, int ServerId)
{
    public bool IsLocal => ServerId == 0;
}

/// <summary>A SQL Agent job with the outcome of its most recent run.</summary>
public sealed record AgentJobRow(
    string Name, bool Enabled, string LastRunOutcome, DateTime? LastRunDate, string Category)
{
    public string Detail => $"{(Enabled ? "Enabled" : "Disabled")} · last run: {LastRunOutcome}"
        + (LastRunDate == null ? "" : $" {LastRunDate:yyyy-MM-dd HH:mm}");
}
