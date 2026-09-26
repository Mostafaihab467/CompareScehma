# `Models/ServerBrowserModels.cs`

**Purpose:** Rows behind the server-scope tree nodes: instance overview, databases with state and size, logins and server roles, linked servers, Agent jobs.

**Namespace:** `SchemaCompare.Models`

## Declared types

- `record ServerOverview` (string ServerName, string Version, string Edition, string Collation) — What the instance reports about itself, shown on the server node.
- `record ServerDatabaseRow` (string Name, string State, long SizeMb) — One database on the instance.
- `record ServerPrincipalRow` (string Name, string TypeDesc, string DefaultDatabase, bool IsDisabled) — A server login or server role from sys.server_principals.
- `record LinkedServerRow` (string Name, string Product, string DataSource, int ServerId) — A linked (distributed-query) server.
- `record AgentJobRow` (     string Name, bool Enabled, string LastRunOutcome, DateTime? LastRunDate, string Category) — A SQL Agent job with the outcome of its most recent run.

## Public surface

`ServerOverview()`, `ServerDatabaseRow()`, `ServerPrincipalRow()`, `LinkedServerRow()`, `AgentJobRow()`

## Referenced by

- `Services/DbManagerService.cs`

**Size:** 43 lines
